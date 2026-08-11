using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.Personnel;

namespace LivingCity.UI
{
    /// <summary>
    /// The personnel ledger: a full-screen book, opened with P, where the player reads and
    /// re-deals his outfit - selection, promotion, reassignment, and who signs out which
    /// pistol. Built for sixty men even though the game opens with six: grouping, sorting
    /// and filtering are the screen, not decoration.
    ///
    /// This is the project's SECOND canvas with a GraphicRaycaster (ContextMenuUI carries
    /// the first and documents the rule). The full-page background is a raycast target ON
    /// PURPOSE - it IS the modal shield: with it under the pointer, every world picker's
    /// existing IsPointerOverGameObject guard stands down, so the city cannot be clicked
    /// through the book. sortingOrder 110: above every readout (overlay 90, wanted 95,
    /// clock 100), below the context menu's 120 - though the menu cannot open while the
    /// book is up, because InteractionController returns early on IsOpen.
    ///
    /// Esc is POLLED here and in two other readers, and polled input cannot be consumed -
    /// so the almanac exposes ClaimsEsc (true while open AND on the frame it closes), and
    /// every other Esc reader yields on it. A future Esc reader must do the same.
    ///
    /// Construction follows ContextMenuUI to the letter: same scaler, page built ACTIVE
    /// then hidden (a TextMeshProUGUI only loads its font in OnEnable, which never runs
    /// under an inactive parent), TMP_Settings guarded, reference-pixel sizes. Stars are
    /// UiSkin's baked gold sprites (full / half / empty - the half is baked, no masks)
    /// because the TMP default font has no star glyph to trust. Mugshots and armory
    /// photos come from PortraitStudio, which photographs the city's own prefabs.
    ///
    /// Repaint is the versioned rebuild the HUDs use: the list and detail card are torn
    /// down and rebuilt when PersonnelDirector.Version or any local view state moves.
    /// Mutations are click-paced, so a few hundred objects per rebuild is the affordable
    /// choice ContextMenuUI already made; pooling waits for a profiler to ask for it.
    /// </summary>
    public sealed class PersonnelAlmanac : MonoBehaviour, IMapTargetingConsumer
    {
        const int SortingOrder = 110;

        /// <summary>The sheet is the LEFT HALF of the screen now - the strategic map
        /// owns the right half while the book is open. 960 is half the 1920-reference
        /// canvas; on wider screens the paper stretches and the extra stays margin.</summary>
        const float PageWidth = 960f;

        const float ListLeft = 40f;
        const float ListWidth = 470f;

        /// <summary>Pages start below the masthead, the tab strip and their rules.</summary>
        const float PageTop = -128f;

        /// <summary>The personnel list sits under its own filter bar inside the page.</summary>
        const float ListTop = -176f;
        const float ListHeight = 860f;

        /// <summary>Widened at the user's word: the card starts a touch after the
        /// list (which ends at 510) and runs nearly to the fold - reference pixels
        /// the character sheet was starving for.</summary>
        const float DetailLeft = 516f;
        const float DetailWidth = PageWidth - DetailLeft - 16f;

        /// <summary>Air between the card's frame and everything printed on it - the
        /// padding the sheet went without. Card content reasons in CardInnerWidth;
        /// DetailWidth is the panel's outer measure only.</summary>
        const float CardPad = 14f;
        const float CardInnerWidth = DetailWidth - CardPad * 2f;

        /// <summary>The wash behind the mugshot block - covers the photo and the
        /// identity lines, kept INSIDE the padding so nothing pokes past the panel's
        /// drawn face (the pack sprite's face sits inset from its rect).</summary>
        const float CardMastheadHeight = 108f;

        /// <summary>Right edge of usable page content, mirroring ListLeft's margin.</summary>
        const float PageRight = PageWidth - 36f;

        /// <summary>The crew band is GONE at the user's word - the lieutenant's row is
        /// the crew's handle (select him; join his crew in assign mode). This gap is
        /// all that separates one crew from the next, and with the hoods indented it
        /// is all the grouping the eye needs.</summary>
        const float CrewGap = 10f;
        const float SectionHeaderHeight = 38f;
        const float RowHeight = 34f;
        const float HoodIndent = 28f;

        /// <summary>Reference pixels of list travel per scroll unit. Tuned for one wheel
        /// notch to move about a row; trackpads arrive as many small deltas and feel the
        /// same.</summary>
        const float WheelStep = 30f;

        // The gold star sprite carries its own empty margin, so it runs a little larger
        // than the diamond it replaced without crowding the pitch.
        const float StarSize = 21f;
        const float StarPitch = 24f;

        /// <summary>True while the book is open. Every world-input reader checks this -
        /// the keyboard half of the modal shield (the raycast-target page is the pointer
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

        enum Confirm
        {
            None,
            Promote,
            Demote,
        }

        /// <summary>The book's sections, in tab-strip order. Newspaper is the entry
        /// page - the player opens the book on what the world thinks of him, then turns
        /// to the working pages.</summary>
        public enum LedgerPage
        {
            Newspaper,
            Personnel,
            Finances,
            Armory,
            Diplomacy,
            Orders,
        }

        Canvas canvas;
        GameObject page;
        Image shadeImage;
        GameObject paperGo;
        TMP_Text titleText;

        LedgerPage currentPage = LedgerPage.Newspaper;
        readonly GameObject[] pageRoots = new GameObject[6];
        readonly Image[] tabFaces = new Image[6];
        readonly TMP_Text[] tabLabels = new TMP_Text[6];
        RectTransform listViewport;
        RectTransform listContent;
        RectTransform detailContent;
        RectTransform hoverNote;
        TMP_Text hoverNoteText;
        GameObject sortMenu;
        TMP_Text titleCount;
        TMP_Text sortLabel;
        TMP_Text rankLabel;
        TMP_Text postLabel;
        TMP_Text showLabel;

        PersonnelDirector director;
        OutfitDirector outfit;
        PlayerMafioso player;
        bool tmpReady;

        /// <summary>Scratch for Turf reads - refilled from the markers on use.</summary>
        readonly System.Collections.Generic.List<Outfit.Turf.Holding> holdings =
            new System.Collections.Generic.List<Outfit.Turf.Holding>();

        /// <summary>selectedId's sentinel for "the front is selected" - the boss's
        /// card rather than a member's. Never a real Character id (those are >= 0).</summary>
        const int FrontSelection = -2;

        ViewOptions options;
        int selectedId = -1;
        bool assignMode;
        Confirm pendingConfirm;
        string lastRefusal = "";
        float scrollY;

        int paintedVersion = -1;
        bool dirty;

        readonly List<LedgerRow> rows = new List<LedgerRow>();
        readonly List<RosterEquipment> held = new List<RosterEquipment>();

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

            tmpReady = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
            if (!tmpReady)
            {
                // Unlike the overlay, which keeps its markers, a ledger without text is
                // nothing at all - so the whole screen sits this session out.
                Debug.LogWarning("[Almanac] No TMP default font - the personnel ledger is " +
                                 "disabled until TMP essentials are imported " +
                                 "(Tools/City/Import TMP Essentials).", this);
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BuildCanvas();
            BuildPage();
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

            // [ and ] turn the book's pages; the tabs are the pointer's way.
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
                scrollY = 0f;
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
                switch (currentPage)
                {
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
                UpdateBarLabels();
            }
        }

        int paintedOutfitVersion = -1;
        int paintedGangVersion = -1;

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
            // The book always opens on the newspaper - the week's narrative frame -
            // and the working pages keep their state for when the player turns to them.
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
            if (sortMenu)
                sortMenu.SetActive(false);
        }

        void UpdateScroll()
        {
            // Each page nominates its one scrolling region; the wheel means nothing
            // anywhere else on the sheet.
            RectTransform viewport;
            RectTransform content;
            switch (currentPage)
            {
                case LedgerPage.Personnel:
                    viewport = listViewport;
                    content = listContent;
                    break;
                case LedgerPage.Armory:
                    viewport = armoryInventoryViewport;
                    content = armoryInventoryContent;
                    break;
                case LedgerPage.Orders:
                    viewport = ordersViewport;
                    content = ordersContent;
                    break;
                default:
                    return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !content)
                return;

            var wheel = mouse.scroll.ReadValue().y;
            if (wheel == 0f)
                return;

            // Only while the pointer is over the list - the rest of the page is fixed
            // and the wheel must not surprise-scroll a list the player is not reading.
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    viewport, mouse.position.ReadValue()))
                return;

            var viewportHeight = viewport.rect.height;
            var maxScroll = Mathf.Max(0f, content.sizeDelta.y - viewportHeight);
            switch (currentPage)
            {
                case LedgerPage.Personnel:
                    scrollY = Mathf.Clamp(scrollY - wheel * WheelStep, 0f, maxScroll);
                    content.anchoredPosition = new Vector2(0f, scrollY);
                    break;
                case LedgerPage.Armory:
                    armoryScrollY =
                        Mathf.Clamp(armoryScrollY - wheel * WheelStep, 0f, maxScroll);
                    content.anchoredPosition = new Vector2(0f, armoryScrollY);
                    break;
                default:
                    ordersScrollY =
                        Mathf.Clamp(ordersScrollY - wheel * WheelStep, 0f, maxScroll);
                    content.anchoredPosition = new Vector2(0f, ordersScrollY);
                    break;
            }
        }

        // -------------------------------------------------------------- construction

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
            // Expand, NOT the HUDs' match-height: match-height clips the page's sides on
            // any window narrower than 16:9 (the first Play did exactly that). Expand
            // guarantees the canvas is at least 1920x1080 in reference units both ways,
            // so the fixed-size sheet always fits whole.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            go.AddComponent<GraphicRaycaster>();
        }

        void BuildPage()
        {
            page = new GameObject("Page", typeof(RectTransform));
            page.transform.SetParent(canvas.transform, false);
            var pageRect = (RectTransform)page.transform;
            pageRect.anchorMin = Vector2.zero;
            pageRect.anchorMax = Vector2.one;
            pageRect.offsetMin = pageRect.offsetMax = Vector2.zero;

            // The modal shield for the BOOK's half only: an opaque backing that also
            // makes IsPointerOverGameObject true over the whole left half. The right
            // half carries the docked strategic map and must see the pointer - so the
            // page root itself has no Image and no raycast presence there.
            var shade = NewRect("Shade", page.transform);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(0.5f, 1f);
            shade.offsetMin = shade.offsetMax = Vector2.zero;
            shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.sprite = null;
            shadeImage.color = LedgerPalette.Room;
            shadeImage.raycastTarget = true;

            // The tube fills the whole left half, edge to edge - no casing, no black
            // margins. Its mask clips anything a page lays past the fold, so nothing
            // ever bleeds over the map beside it.
            var paper = NewRect("Paper", page.transform);
            paperGo = paper.gameObject;
            paper.anchorMin = Vector2.zero;
            paper.anchorMax = new Vector2(0.5f, 1f);
            paper.offsetMin = paper.offsetMax = Vector2.zero;
            var paperImage = paper.gameObject.AddComponent<Image>();
            paperImage.sprite = null;
            paperImage.color = LedgerPalette.Screen;
            paperImage.raycastTarget = false;
            paper.gameObject.AddComponent<RectMask2D>();

            // Depth without ruled lines: the pack vignette breathes dark at the page
            // edges, and the masthead band floors the top row - the dividers this
            // page used to draw are gone at the user's word.
            var vignetteSprite = LedgerSkinSet.Vignette;
            if (vignetteSprite != null)
            {
                var vignette = NewRect("Vignette", paper);
                vignette.anchorMin = Vector2.zero;
                vignette.anchorMax = Vector2.one;
                vignette.offsetMin = vignette.offsetMax = Vector2.zero;
                var vignetteImage = vignette.gameObject.AddComponent<Image>();
                vignetteImage.sprite = vignetteSprite;
                vignetteImage.type = Image.Type.Sliced;
                vignetteImage.pixelsPerUnitMultiplier = 1.5f;
                vignetteImage.color = new Color(0f, 0f, 0f, 0.5f);
                vignetteImage.raycastTarget = false;
            }

            BuildTitleBar(paper);
            BuildTabs(paper);

            // ---- the Personnel page, a full-stretch sheet like every other page ----
            var personnel = NewPageRoot(paper, LedgerPage.Personnel);

            BuildFilterBar(personnel);

            // List viewport: RectMask2D clips without needing an Image of its own.
            listViewport = NewRect("Roster", personnel);
            PlaceTopLeft(listViewport, ListLeft, ListTop, ListWidth, ListHeight);
            listViewport.gameObject.AddComponent<RectMask2D>();

            listContent = NewRect("Content", listViewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, ListHeight);

            var detailBack = NewRect("Detail", personnel);
            PlaceTopLeft(detailBack, DetailLeft, ListTop, DetailWidth, ListHeight);
            var detailImage = detailBack.gameObject.AddComponent<Image>();
            detailImage.sprite = null;
            detailImage.color = LedgerPalette.CardTint;
            detailImage.raycastTarget = false;
            // The pack panel carries its own frame, re-cast to the tube's green - the
            // one blue panel on a phosphor page read as a stranger's furniture. The
            // sprite-less flat tint keeps the drawn frame.
            if (!LedgerSkinSet.TryDressPanel(detailImage, LedgerPalette.CardFace))
                Frame(detailBack, 1f, LedgerPalette.PhosphorDim);

            // The content rect sits INSIDE the frame by CardPad on every side, so
            // every line the card prints inherits the padding for free.
            detailContent = NewRect("DetailContent", detailBack);
            detailContent.anchorMin = Vector2.zero;
            detailContent.anchorMax = Vector2.one;
            detailContent.offsetMin = new Vector2(CardPad, CardPad);
            detailContent.offsetMax = new Vector2(-CardPad, -CardPad);

            // The one shared hover note - a child of the CARD, not the content (which
            // rebuilds under the pointer), raised to last sibling on every show so it
            // prints over whatever rows it happens to cover.
            hoverNote = NewRect("HoverNote", detailBack);
            PlaceTopLeft(hoverNote, CardPad, -CardPad, CardInnerWidth, 60f);
            var noteImage = hoverNote.gameObject.AddComponent<Image>();
            noteImage.sprite = null;
            noteImage.color = LedgerPalette.Screen;
            noteImage.raycastTarget = false;
            Frame(hoverNote, 1f, LedgerPalette.PhosphorDim);
            hoverNoteText = NewText("Text", hoverNote, 12.5f, LedgerPalette.Phosphor,
                TextAlignmentOptions.TopLeft);
            hoverNoteText.rectTransform.anchorMin = Vector2.zero;
            hoverNoteText.rectTransform.anchorMax = Vector2.one;
            hoverNoteText.rectTransform.offsetMin = new Vector2(10f, 8f);
            hoverNoteText.rectTransform.offsetMax = new Vector2(-10f, -8f);
            hoverNoteText.textWrappingMode = TextWrappingModes.Normal;
            hoverNote.gameObject.SetActive(false);

            BuildSortMenu(personnel);

            // ---- the other sheets ----
            BuildNewspaperPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildDiplomacyPage(paper);
            // Orders is a page of the book like the rest - the strategic map it aims
            // at stands permanently open in the right half of the screen.
            BuildOrdersPage(paper);

            SetPage(LedgerPage.Newspaper);

            // LAST children, so hierarchy order draws them over everything on the tube:
            // the raster's dark lines, drawn once - the dynamic rebuilds only ever touch
            // listContent and detailContent, which are earlier siblings.
            BuildScanlines(paper);

            // Built active for TMP's sake, hidden until P.
            page.SetActive(false);
        }

        RectTransform NewPageRoot(RectTransform paper, LedgerPage kind)
        {
            var root = NewRect("Page " + kind, paper);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            pageRoots[(int)kind] = root.gameObject;
            return root;
        }

        /// <summary>The tab strip - six soft-keys under the masthead. The active tab
        /// runs inverse video, the same highlight the roster rows use, so "where am I"
        /// reads by the one convention the whole terminal has.</summary>
        void BuildTabs(RectTransform paper)
        {
            var names = new[]
                { "NEWSPAPER", "PERSONNEL", "FINANCES", "ARMORY", "DIPLOMACY", "ORDERS" };
            // Packed so the VISIBLE edges touch, not the rects: the pack slab carries
            // transparent margins inside its own sprite (measured off Button_04's
            // alpha: 47px left + 37px right, /3.5 PPU = 24 reference units of air), so
            // flush rects still LOOK gapped. The step overlaps exactly that air; the
            // raycast seam under the overlap goes to the later sibling, which is a
            // hairline nobody aims at.
            const float width = 139f;
            const float step = width - 24f;

            for (var i = 0; i < names.Length; i++)
            {
                var kind = (LedgerPage)i;
                var rect = NewRect("Tab " + names[i], paper);
                PlaceTopLeft(rect, ListLeft + i * step, -74f, width, 36f);

                var face = rect.gameObject.AddComponent<Image>();
                face.sprite = null;
                face.color = LedgerPalette.ButtonGlow;
                face.raycastTarget = true;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                // Dressing order is a chain of wardrobes: the Modern Menus slab first,
                // the Waste No Space sheet if the pack is missing, and the flat block
                // in its 1px frame when there is no sprite at all. RefreshTabs says
                // "active" in whichever language the strip ended up wearing.
                if (LedgerSkinSet.TryDressTab(button, face))
                    skinnedTabs = true;
                else if (!UiSkin.TryDressButton(button, face))
                {
                    var colours = button.colors;
                    colours.normalColor = LedgerPalette.ButtonNormal;
                    colours.highlightedColor = LedgerPalette.ButtonHover;
                    colours.pressedColor = LedgerPalette.ButtonPressed;
                    button.colors = colours;
                    Frame(rect, 1f, LedgerPalette.PhosphorDim);
                }
                button.onClick.AddListener(() => SetPage(kind));

                var label = NewText("Label", rect, 13f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.Center);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                label.characterSpacing = 2f;
                label.fontStyle = FontStyles.Bold;
                label.text = names[i];
                LedgerSkinSet.ApplyHeadline(label);

                tabFaces[i] = face;
                tabLabels[i] = label;
            }
        }

        /// <summary>Set once in BuildTabs when the Modern Menus slab dressed the strip -
        /// RefreshTabs cannot tell the two sprite wardrobes apart by looking at the
        /// Image, and each says "active" differently.</summary>
        bool skinnedTabs;

        void RefreshTabs()
        {
            for (var i = 0; i < tabFaces.Length; i++)
            {
                if (!tabFaces[i])
                    continue;
                var active = i == (int)currentPage;
                // The pack strip says "selected" in accent blue with the label gone
                // inverse; UiSkin's tabs sit pressed into the case; the sprite-less
                // fallback keeps the terminal's inverse video.
                if (skinnedTabs)
                {
                    tabFaces[i].color = active
                        ? LedgerSkinSet.AccentTint : LedgerSkinSet.FaceTint;
                    tabLabels[i].color = active
                        ? LedgerPalette.Screen : LedgerPalette.Phosphor;
                }
                else if (tabFaces[i].sprite != null)
                {
                    tabFaces[i].sprite = active ? UiSkin.Sunken : UiSkin.ButtonNormal;
                    tabLabels[i].color = active
                        ? LedgerPalette.Phosphor : LedgerPalette.PhosphorDim;
                }
                else
                {
                    tabFaces[i].color = active
                        ? LedgerPalette.Phosphor : LedgerPalette.ButtonGlow;
                    tabLabels[i].color = active
                        ? LedgerPalette.Screen : LedgerPalette.Phosphor;
                }
            }
        }

        /// <summary>
        /// Turns the book to a page. Page STATE persists - the personnel selection,
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

        /// <summary>A sheet that exists but is not written yet - the honest placeholder
        /// while the ledger's pages land one by one.</summary>
        void BuildComingPage(RectTransform paper, LedgerPage kind, string name)
        {
            var root = NewPageRoot(paper, kind);
            var hint = NewText("Hint", root, 16f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.Center);
            hint.rectTransform.anchorMin = Vector2.zero;
            hint.rectTransform.anchorMax = Vector2.one;
            hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
            hint.characterSpacing = 3f;
            hint.text = "==  " + name + "  ==\n\nPAGE NOT YET IN SERVICE";
            hint.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>
        /// The entry page: a wire-service front page on the tube. One static edition for
        /// now, laid out in the parts a generated weekly front page will fill later -
        /// the named rects (Lead, Photo, Column 0..2) ARE the reservation, so the
        /// generator slots into this layout instead of redesigning it.
        /// </summary>
        void BuildNewspaperPage(RectTransform paper)
        {
            var root = NewPageRoot(paper, LedgerPage.Newspaper);
            var fullWidth = PageWidth - ListLeft - 36f;

            var masthead = NewText("Masthead", root, 44f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            PlaceTopLeft(masthead.rectTransform, ListLeft, PageTop - 8f, fullWidth, 64f);
            masthead.fontStyle = FontStyles.Bold;
            masthead.characterSpacing = 10f;
            masthead.text = "THE CITY WIRE";
            LedgerSkinSet.ApplyHeadline(masthead);

            newspaperDateline = NewText("Dateline", root, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.Center);
            PlaceTopLeft(newspaperDateline.rectTransform, ListLeft, PageTop - 76f,
                fullWidth, 22f);
            newspaperDateline.characterSpacing = 3f;

            var rule = NewRect("MastheadRule", root);
            PlaceTopLeft(rule, ListLeft, PageTop - 102f, fullWidth, 2f);
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.sprite = null;
            ruleImage.color = LedgerPalette.Phosphor;
            ruleImage.raycastTarget = false;

            // ---- lead story, left of the photo ----
            const float leadWidth = 560f;
            var lead = NewRect("Lead", root);
            PlaceTopLeft(lead, ListLeft, PageTop - 116f, leadWidth, 320f);

            var headline = NewText("Headline", lead, 26f, LedgerPalette.Phosphor,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(headline.rectTransform, 0f, 0f, leadWidth, 46f);
            headline.fontStyle = FontStyles.Bold;
            headline.characterSpacing = 2f;
            headline.text = "CRIME COMES TO THE CITY";

            var subhead = NewText("Subhead", lead, 16f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(subhead.rectTransform, 0f, -50f, leadWidth, 24f);
            subhead.fontStyle = FontStyles.Italic;
            subhead.text =
                "New outfit takes a front on the waterfront — City Hall says nothing";

            var body = NewParagraph("LeadBody", lead, 14.5f, LedgerPalette.Phosphor);
            PlaceTopLeft(body.rectTransform, 0f, -84f, leadWidth, 220f);
            body.text =
                "By a Staff Correspondent — Something has changed on the avenues. Men " +
                "in good coats keep hours no shopkeeper keeps, and the cafes pour for " +
                "strangers who pay from fresh rolls of bills. Deliveries arrive after " +
                "dark at addresses that order nothing. Asked whether an organization " +
                "has taken root in the city, the police commissioner said only that " +
                "the matter is receiving attention, and that honest citizens have " +
                "nothing to fear. On the waterfront, nobody laughed.";

            // ---- wirephoto, right column ----
            const float photoLeft = 624f;
            const float photoWidth = PageRight - photoLeft;
            var photo = NewRect("Photo", root);
            PlaceTopLeft(photo, photoLeft, PageTop - 116f, photoWidth, 300f);
            var photoImage = photo.gameObject.AddComponent<Image>();
            photoImage.sprite = null;
            photoImage.color = LedgerPalette.PhotoBack;
            photoImage.raycastTarget = false;
            Frame(photo, 1f, LedgerPalette.PhosphorDim);

            var photoMark = NewText("Mark", photo, 18f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.Center);
            photoMark.rectTransform.anchorMin = Vector2.zero;
            photoMark.rectTransform.anchorMax = Vector2.one;
            photoMark.rectTransform.offsetMin = photoMark.rectTransform.offsetMax =
                Vector2.zero;
            photoMark.characterSpacing = 8f;
            photoMark.text = "W I R E P H O T O";

            var caption = NewText("Caption", root, 12.5f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(caption.rectTransform, photoLeft, PageTop - 422f,
                photoWidth, 40f);
            caption.fontStyle = FontStyles.Italic;
            caption.textWrappingMode = TextWrappingModes.Normal;
            caption.text = "The waterfront at dusk, where the new money drinks.";

            // ---- body columns under the lead, full page width ----
            const float columnGap = 28f;
            var columnWidth = (fullWidth - 2f * columnGap) / 3f;
            var columnTexts = new[]
            {
                "MARKETS — Dock tonnage is up for the third week running; the port " +
                "authority credits mild weather and says nothing about the new " +
                "warehouse leases. Wholesale prices firm. Three builders' merchants " +
                "report a shortage of copper pipe nobody can quite explain.",

                "CITY DESK — The school board voted funds for a crossing guard at the " +
                "elementary. Complaints of noise on the ring road continue. A camper " +
                "van, twice reported stolen this month, was twice found parked " +
                "outside the same tavern; the owner declines to press charges.",

                "WEATHER — Smog expected to sit over the harbor through the weekend; " +
                "visibility poor on the water after dark. ADVERTISEMENT — MARLOWE'S " +
                "FINE TAILORING: suits cut for the discreet professional. Fittings " +
                "by appointment only.",
            };

            for (var i = 0; i < 3; i++)
            {
                var column = NewParagraph("Column " + i, root, 13f,
                    LedgerPalette.PhosphorDim);
                PlaceTopLeft(column.rectTransform,
                    ListLeft + i * (columnWidth + columnGap),
                    PageTop - 472f, columnWidth, 380f);
                column.text = columnTexts[i];
            }
        }

        static TextMeshProUGUI NewParagraph(string name, Transform parent, float size,
            Color color)
        {
            var text = NewText(name, parent, size, color, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.lineSpacing = 8f;
            return text;
        }

        TMP_Text newspaperDateline;

        // ------------------------------------------------------------- the finances page

        RectTransform financesContent;

        /// <summary>How many weeks back the sheet is turned; 0 = the open week.</summary>
        int financeWeekBack;

        void BuildFinancesPage(RectTransform paper)
        {
            var root = NewPageRoot(paper, LedgerPage.Finances);

            NewToolbarButton(root, "< EARLIER", PageRight - 253f, PageTop, 130f, 32f, () =>
            {
                var sheets = outfit ? outfit.Accounts.Sheets.Count : 1;
                if (financeWeekBack < sheets - 1)
                {
                    financeWeekBack++;
                    dirty = true;
                }
            });
            NewToolbarButton(root, "LATER >", PageRight - 130f, PageTop, 130f, 32f, () =>
            {
                if (financeWeekBack > 0)
                {
                    financeWeekBack--;
                    dirty = true;
                }
            });

            financesContent = NewRect("Sheet", root);
            financesContent.anchorMin = Vector2.zero;
            financesContent.anchorMax = Vector2.one;
            financesContent.offsetMin = financesContent.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Paints the balance sheet. EVERY figure is derived at this moment from game
        /// state - wages from the live roster via Wages.WeeklyPayroll, totals through
        /// BalanceMath - never a stored display string; hire a man and this page moves
        /// the same frame. Historical sheets are closed records and say so.
        /// </summary>
        void RebuildFinances()
        {
            foreach (Transform old in financesContent)
                Destroy(old.gameObject);

            if (!outfit)
                return;

            var accounts = outfit.Accounts;
            var index = accounts.Sheets.Count - 1 - financeWeekBack;
            if (index < 0)
                index = 0;
            var sheet = accounts.Sheets.Count > 0 ? accounts.Sheets[index] : null;
            var roster = director.Roster;

            var report = Outfit.FinanceReport.For(
                sheet,
                Outfit.Wages.WeeklyPayroll(roster),
                accounts.Safe,
                accounts.RiskyMoney,
                Outfit.BalanceMath.AssetsOf(roster));

            var heading = NewText("Heading", financesContent, 18f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(heading.rectTransform, ListLeft, PageTop, 600f, 32f);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 2f;
            heading.text = "WEEKLY BALANCE SHEET — WEEK " + report.Week +
                           (report.Closed ? "  [CLOSED]" : "");

            // ---- income left, outgoings right ----
            var y = PageTop - 48f;

            FinanceRow(ListLeft, y, ":: INCOME", "", LedgerPalette.PhosphorDim, false);
            FinanceRow(ListLeft + 460f, y, ":: OUTGOINGS", "", LedgerPalette.PhosphorDim,
                false);
            y -= 30f;

            FinanceRow(ListLeft, y, "Legal", LedgerText.Cash(report.LegalIncome),
                LedgerPalette.Phosphor, false);
            FinanceRow(ListLeft + 460f, y, "Wages",
                LedgerText.Cash(report.Wages), LedgerPalette.Phosphor, false);
            y -= 26f;

            FinanceRow(ListLeft, y, "Illegal", LedgerText.Cash(report.IllegalIncome),
                LedgerPalette.Phosphor, false);
            // The payroll breakdown, right where the biggest number is born - the
            // player going broke must SEE that the wage bill is his own roster.
            var hoods = 0;
            var lieutenants = 0;
            var specialists = 0;
            var hoodWages = 0;
            var lieutenantWages = 0;
            var specialistWages = 0;
            if (roster != null)
                foreach (var member in roster.Members)
                {
                    var wage = Outfit.Wages.WageFor(member);
                    if (member.Specialty != Specialty.None)
                    {
                        specialists++;
                        specialistWages += wage;
                    }
                    else if (member.Rank == Rank.Lieutenant)
                    {
                        lieutenants++;
                        lieutenantWages += wage;
                    }
                    else
                    {
                        hoods++;
                        hoodWages += wage;
                    }
                }

            FinanceRow(ListLeft + 460f, y, "   " + hoods + " hoods",
                LedgerText.Cash(hoodWages), LedgerPalette.PhosphorDim, false);
            y -= 26f;

            FinanceRow(ListLeft, y, "Sales", LedgerText.Cash(report.SalesIncome),
                LedgerPalette.Phosphor, false);
            FinanceRow(ListLeft + 460f, y, "   " + lieutenants + " lieutenants",
                LedgerText.Cash(lieutenantWages), LedgerPalette.PhosphorDim, false);
            y -= 26f;

            if (specialists > 0)
            {
                FinanceRow(ListLeft + 460f, y, "   " + specialists + " on retainer",
                    LedgerText.Cash(specialistWages), LedgerPalette.PhosphorDim, false);
                y -= 26f;
            }

            FinanceRow(ListLeft + 460f, y, "Bribes", LedgerText.Cash(report.Bribes),
                LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft + 460f, y, "Purchases", LedgerText.Cash(report.Purchases),
                LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft + 460f, y, "Other costs", LedgerText.Cash(report.OtherCosts),
                LedgerPalette.Phosphor, false);
            y -= 34f;

            FinanceRow(ListLeft, y, "TOTAL IN", LedgerText.Cash(report.TotalIncome),
                LedgerPalette.Phosphor, true);
            FinanceRow(ListLeft + 460f, y, "TOTAL OUT", LedgerText.Cash(report.TotalOutgoings),
                LedgerPalette.Phosphor, true);
            y -= 40f;

            // ---- the derived run, full width ----
            var profitColor = report.Profit < 0 ? LedgerPalette.Amber : LedgerPalette.Phosphor;
            FinanceRow(ListLeft, y, "PROFIT", LedgerText.Cash(report.Profit), profitColor,
                true);
            y -= 28f;
            FinanceRow(ListLeft, y, "Tax due (" + Outfit.BalanceMath.TaxRatePercent + "%)",
                LedgerText.Cash(report.TaxDue), LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft, y, "Tax paid", LedgerText.Cash(report.TaxPaid),
                LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft, y, "TOTAL PROFIT (after tax)",
                LedgerText.Cash(report.TotalProfit), profitColor, true);
            y -= 36f;

            // Stocks are NOW-figures; a closed week's page keeps to its flows.
            if (!report.Closed)
            {
                var riskColor = report.Risk >= Outfit.RiskRating.Moderate
                    ? LedgerPalette.Amber
                    : LedgerPalette.Phosphor;
                FinanceRow(ListLeft, y, "Risky money (unlaundered)",
                    LedgerText.Cash(report.RiskyMoney), riskColor, false);
                y -= 26f;
                FinanceRow(ListLeft, y, "Risk",
                    LedgerText.RiskLabel(report.Risk).ToUpperInvariant(), riskColor,
                    report.Risk >= Outfit.RiskRating.Moderate);
                y -= 36f;

                FinanceRow(ListLeft, y, "MONEY IN SAFE", LedgerText.Cash(report.Safe),
                    LedgerPalette.Phosphor, true);
                y -= 28f;
                FinanceRow(ListLeft, y, "Assets (stock at book value)",
                    LedgerText.Cash(report.Assets), LedgerPalette.Phosphor, false);
                y -= 28f;
                FinanceRow(ListLeft, y, "TOTAL WEALTH", LedgerText.Cash(report.TotalWealth),
                    LedgerPalette.Phosphor, true);
            }
            else
            {
                var stamp = NewText("ClosedStamp", financesContent, 14f,
                    LedgerPalette.PhosphorDim, TextAlignmentOptions.TopLeft);
                PlaceTopLeft(stamp.rectTransform, ListLeft, y, 860f, 40f);
                stamp.text = "A closed week - the record of what moved. Current holdings " +
                             "live on the open sheet.";
                y -= 44f;
            }

            // ---- the plain-language note, under the sheet ----
            var note = NewParagraph("Note", financesContent, 13f, LedgerPalette.PhosphorDim);
            PlaceTopLeft(note.rectTransform, ListLeft, y - 40f,
                PageRight - ListLeft, 240f);
            note.text =
                "Every figure on this sheet is computed from the books as they stand " +
                "this instant. Wages are the whole roster, week in, week out - the " +
                "jailed and the hospitalized stay on the payroll; only the dead come " +
                "off. Recruit a man on the PERSONNEL page and the wage line moves " +
                "before you can turn back to look at it.\n\n" +
                "A big crew with no income is the classic way an outfit dies.";
        }

        void FinanceRow(float x, float y, string label, string value, Color color,
            bool bold)
        {
            var labelText = NewText("Label", financesContent, bold ? 15f : 14f, color,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(labelText.rectTransform, x, y, 290f, 24f);
            if (bold)
                labelText.fontStyle = FontStyles.Bold;
            labelText.text = label;

            if (value.Length == 0)
                return;

            var valueText = NewText("Value", financesContent, bold ? 15f : 14f, color,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(valueText.rectTransform, x + 250f, y, 170f, 24f);
            if (bold)
                valueText.fontStyle = FontStyles.Bold;
            valueText.text = value;
        }

        // --------------------------------------------------------------- the armory page

        RectTransform armoryContent;
        RectTransform armoryInventoryViewport;
        RectTransform armoryInventoryContent;
        float armoryScrollY;

        /// <summary>The stock sits UNDER the catalogue now - the half-width page has no
        /// second column. The catalogue's eight rows end near -694; the stock header and
        /// its scrolling viewport take the rest of the sheet.</summary>
        const float StockHeaderY = -700f;
        const float StockTop = -732f;
        const float StockHeight = 304f;
        const float StockWidth = PageRight - ListLeft;

        /// <summary>The item a GIVE click is finding a holder for; -1 = browsing.</summary>
        int givePickerItemId = -1;

        string armoryNote = "";

        void BuildArmoryPage(RectTransform paper)
        {
            var root = NewPageRoot(paper, LedgerPage.Armory);

            armoryContent = NewRect("Counter", root);
            armoryContent.anchorMin = Vector2.zero;
            armoryContent.anchorMax = Vector2.one;
            armoryContent.offsetMin = armoryContent.offsetMax = Vector2.zero;

            // The inventory scrolls on its own - a sixty-man outfit's stock outgrows
            // the sheet, and the mask is built once so the scroll survives rebuilds.
            armoryInventoryViewport = NewRect("Inventory", root);
            PlaceTopLeft(armoryInventoryViewport, ListLeft, StockTop,
                StockWidth, StockHeight);
            armoryInventoryViewport.gameObject.AddComponent<RectMask2D>();

            armoryInventoryContent = NewRect("Rows", armoryInventoryViewport);
            armoryInventoryContent.anchorMin = new Vector2(0f, 1f);
            armoryInventoryContent.anchorMax = new Vector2(1f, 1f);
            armoryInventoryContent.pivot = new Vector2(0f, 1f);
            armoryInventoryContent.anchoredPosition = Vector2.zero;
            armoryInventoryContent.sizeDelta = new Vector2(0f, StockHeight);
        }

        void RebuildArmory()
        {
            foreach (Transform old in armoryContent)
                Destroy(old.gameObject);
            foreach (Transform old in armoryInventoryContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null)
                return;

            var safe = outfit ? outfit.Accounts.Safe : 0;

            var heading = NewText("Heading", armoryContent, 20f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(heading.rectTransform, ListLeft, PageTop, 400f, 32f);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 3f;
            heading.text = "THE ARMORY";

            var safeText = NewText("Safe", armoryContent, 16f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(safeText.rectTransform, PageWidth - 396f, PageTop, 360f, 32f);
            safeText.fontStyle = FontStyles.Bold;
            safeText.text = "SAFE: " + LedgerText.Cash(safe);

            if (armoryNote.Length > 0)
            {
                var note = NewText("Note", armoryContent, 13f, LedgerPalette.Amber,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(note.rectTransform, ListLeft, PageTop - 34f, StockWidth, 22f);
                note.text = armoryNote;
            }

            BuildCatalogue(roster, safe);

            if (givePickerItemId >= 0)
                BuildGivePicker(roster);
            else
                BuildInventory(roster);
        }

        void BuildCatalogue(Roster roster, int safe)
        {
            var y = PageTop - 76f;
            y = CatalogueHeader(":: CATALOGUE — WEAPONS", y);
            foreach (var item in Outfit.ArmoryCatalog.Weapons)
                y = CatalogueRow(item, safe, y);

            y -= 14f;
            y = CatalogueHeader(":: CATALOGUE — VEHICLES", y);
            foreach (var item in Outfit.ArmoryCatalog.Vehicles)
                y = CatalogueRow(item, safe, y);
        }

        float CatalogueHeader(string label, float y)
        {
            var header = NewText("Header", armoryContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(header.rectTransform, ListLeft, y, 500f, 24f);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 2f;
            header.text = label;
            return y - 30f;
        }

        float CatalogueRow(Outfit.ArmoryItem item, int safe, float y)
        {
            var name = NewText("Name", armoryContent, 15f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(name.rectTransform, ListLeft, y, 260f, 22f);
            name.fontStyle = FontStyles.Bold;
            name.text = item.DisplayName;

            var price = NewText("Price", armoryContent, 15f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(price.rectTransform, ListLeft + 280f, y, 130f, 22f);
            price.text = LedgerText.Cash(item.Price);

            var captured = item;
            var buyLabel = NewButton(armoryContent, "BUY", ListLeft + 440f, y + 1f,
                100f, 24f, () =>
                {
                    var result = outfit
                        ? outfit.Purchase(captured.Price, captured.DisplayName)
                        : OpResult.Fail(LedgerText.ReasonNoSuchItem);
                    if (result.Ok)
                    {
                        director.AddEquipment(captured.Kind, captured.DisplayName,
                            captured.Price);
                        armoryNote = captured.DisplayName + " added to the stock.";
                    }
                    else
                        armoryNote = result.Reason;
                    dirty = true;
                });
            // Short money reads at a glance; the click still explains exactly how short.
            if (safe < item.Price)
                buyLabel.color = LedgerPalette.Disabled;

            var note = NewText("ItemNote", armoryContent, 12f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(note.rectTransform, ListLeft, y - 22f, 560f, 20f);
            note.text = item.Note;

            // The merchandise itself, photographed by PortraitStudio: guns through the
            // LedgerModelSet bridge, cars straight from the city's own PrefabDatabase.
            // The square print is cropped to its middle band by uvRect - the subject is
            // centred, so a 2:1 window keeps it whole. No model, no photo, row unchanged.
            var thumb = NewRect("Thumb", armoryContent);
            PlaceTopLeft(thumb, ListLeft + 562f, y + 2f, 96f, 48f);
            var thumbImage = thumb.gameObject.AddComponent<RawImage>();
            thumbImage.raycastTarget = false;
            thumbImage.enabled = false;
            thumbImage.uvRect = new Rect(0f, 0.26f, 1f, 0.48f);
            var vehicle = item.Kind == Personnel.EquipmentKind.Vehicle;
            var model = vehicle
                ? PortraitStudio.FindVehiclePrefab(
                    PortraitStudio.VehicleModelFor(item.DisplayName))
                : LedgerModelSet.WeaponModelFor(item.Kind);
            PortraitStudio.Request(model,
                vehicle ? PortraitStudio.Framing.Vehicle : PortraitStudio.Framing.Item,
                thumbImage);

            return y - 52f;
        }

        void BuildInventory(Roster roster)
        {
            var header = NewText("InvHeader", armoryContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(header.rectTransform, ListLeft, StockHeaderY, 500f, 24f);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 2f;
            header.text = ":: STOCK — " + roster.Equipment.Count + " ITEMS";

            var y = 0f;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                var row = NewRect("Item", armoryInventoryContent);
                PlaceTopLeft(row, 0f, y, StockWidth, 28f);

                var kind = NewText("Kind", row, 12f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(kind.rectTransform, 0f, 110f);
                kind.text = LedgerText.EquipmentLabel(item.Kind).ToUpperInvariant();

                var name = NewText("Name", row, 14f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(name.rectTransform, 116f, 240f);
                name.text = item.DisplayName;

                var holder = roster.Find(item.HolderId);
                var atFront = item.OwnerId == RosterEquipment.FrontArmory;
                var holderText = NewText("Holder", row, 13f,
                    holder != null || atFront
                        ? LedgerPalette.Phosphor : LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(holderText.rectTransform, 370f, 330f);
                holderText.text = holder != null ? holder.FullName
                    : atFront ? "at the front" : "in armory";

                var itemId = item.Id;
                if (item.OwnerId != RosterEquipment.Unheld)
                    NewButton(row, "RETURN", StockWidth - 120f, -2f,
                        116f, 24f, () =>
                        {
                            var result = director.ReturnEquipment(itemId);
                            armoryNote = result.Ok ? "" : result.Reason;
                            dirty = true;
                        });
                else
                    NewButton(row, "GIVE", StockWidth - 120f, -2f,
                        116f, 24f, () =>
                        {
                            givePickerItemId = itemId;
                            armoryNote = "";
                            dirty = true;
                        });

                y -= 30f;
            }

            SizeInventoryContent(-y);
        }

        /// <summary>GIVE's second step: the stock list becomes the lieutenants, pick
        /// the crew. ALL gear issues through a crew's head - he deals his men in
        /// himself (NormalizeArms: guns by Firearms, wheels by Driving) - so the row
        /// shows the one stat every deal runs on: his Organization.</summary>
        void BuildGivePicker(Roster roster)
        {
            RosterEquipment item = null;
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].Id == givePickerItemId)
                    item = roster.Equipment[i];
            if (item == null)
            {
                givePickerItemId = -1;
                BuildInventory(roster);
                return;
            }

            var header = NewText("PickHeader", armoryContent, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(header.rectTransform, ListLeft, StockHeaderY, 600f, 24f);
            header.fontStyle = FontStyles.Bold;
            header.text = "GIVE " + item.DisplayName.ToUpperInvariant() + " TO:";

            NewButton(armoryContent, "CANCEL", PageRight - 120f, StockHeaderY,
                120f, 24f, () =>
                {
                    givePickerItemId = -1;
                    dirty = true;
                });

            var y = 0f;

            // The front first: the desk is a destination like any lieutenant - gear
            // dumped there arms the men guarding it.
            {
                var frontRow = NewRect("PickFront", armoryInventoryContent);
                PlaceTopLeft(frontRow, 0f, y, StockWidth, 28f);

                var frontPick = NewButton(frontRow, "", 0f, 0f, 300f, 26f, () =>
                {
                    var result = director.GiveEquipmentToFront(givePickerItemId);
                    armoryNote = result.Ok ? "" : result.Reason;
                    givePickerItemId = -1;
                    dirty = true;
                });
                frontPick.alignment = TextAlignmentOptions.MidlineLeft;
                frontPick.margin = new Vector4(10f, 0f, 0f, 0f);
                frontPick.text = "THE FRONT";

                var frontGuards = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Status == CharacterStatus.Active &&
                        RosterOps.InFrontGuard(roster, roster.Members[i].Id))
                        frontGuards++;
                var frontMen = NewText("Crew", frontRow, 12f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(frontMen.rectTransform, 540f, 220f);
                frontMen.text = frontGuards > 0
                    ? "deals to " + frontGuards + " men"
                    : "nobody on guard";

                y -= 30f;
            }

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Dead ||
                    member.Rank != Rank.Lieutenant)
                    continue;

                var row = NewRect("Pick", armoryInventoryContent);
                PlaceTopLeft(row, 0f, y, StockWidth, 28f);

                var memberId = member.Id;
                var pick = NewButton(row, "", 0f, 0f, 300f, 26f, () =>
                {
                    var result = director.GiveEquipment(givePickerItemId, memberId);
                    armoryNote = result.Ok ? "" : result.Reason;
                    givePickerItemId = -1;
                    dirty = true;
                });
                pick.alignment = TextAlignmentOptions.MidlineLeft;
                pick.margin = new Vector4(10f, 0f, 0f, 0f);
                pick.text = member.FullName.ToUpperInvariant();

                var stars = NewText("Stat", row, 13f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(stars.rectTransform, 320f, 200f);
                stars.text = LedgerText.AttributeLabel(CharacterAttribute.Organization) +
                    " " + LedgerText.Stars(
                        member.GetHalfSteps(CharacterAttribute.Organization));

                var crew = roster.CrewOf(memberId);
                var men = NewText("Crew", row, 12f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(men.rectTransform, 540f, 220f);
                men.text = crew != null
                    ? "deals to " + crew.HoodIds.Count + " men"
                    : "no crew yet";

                y -= 30f;
            }

            SizeInventoryContent(-y);
        }

        void SizeInventoryContent(float height)
        {
            const float viewportHeight = StockHeight;
            armoryInventoryContent.sizeDelta =
                new Vector2(0f, Mathf.Max(viewportHeight, height + 8f));
            var maxScroll = Mathf.Max(0f, armoryInventoryContent.sizeDelta.y - viewportHeight);
            armoryScrollY = Mathf.Clamp(armoryScrollY, 0f, maxScroll);
            armoryInventoryContent.anchoredPosition = new Vector2(0f, armoryScrollY);
        }

        // ------------------------------------------------------------ the diplomacy page

        RectTransform diplomacyContent;

        void BuildDiplomacyPage(RectTransform paper)
        {
            var root = NewPageRoot(paper, LedgerPage.Diplomacy);
            diplomacyContent = NewRect("Families", root);
            diplomacyContent.anchorMin = Vector2.zero;
            diplomacyContent.anchorMax = Vector2.one;
            diplomacyContent.offsetMin = diplomacyContent.offsetMax = Vector2.zero;
        }

        void RebuildDiplomacy()
        {
            foreach (Transform old in diplomacyContent)
                Destroy(old.gameObject);

            var heading = NewText("Heading", diplomacyContent, 20f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(heading.rectTransform, ListLeft, PageTop, 700f, 32f);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 3f;
            heading.text = "FAMILIES OF THE CITY";

            var gangs = Gangs.GangRegistry.Gangs;
            if (gangs.Count == 0)
            {
                var waiting = NewText("Waiting", diplomacyContent, 14f,
                    LedgerPalette.PhosphorDim, TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(waiting.rectTransform, ListLeft, PageTop - 48f, 800f, 24f);
                waiting.text = "The families have not shown themselves yet.";

                // DEV, editor only: deal a dummy hand of families so the page can be
                // seen dressed before the street layer seeds the real ones. The real
                // generator with a fixed seed, so the preview IS the live layout; a
                // later GangDirector install overwrites it wholesale, and the Version
                // bump repaints this page on its own.
                if (Application.isEditor)
                    NewCardKey(diplomacyContent, "DEAL DUMMY FAMILIES", ListLeft,
                        PageTop - 88f, 240f, 32f, false, () =>
                            Gangs.GangRegistry.Install(
                                Gangs.GangSeeder.Generate(1987, director.Roster)));
                return;
            }

            if (outfit)
                outfit.CollectHoldings(holdings);
            else
                holdings.Clear();
            var y = PageTop - 52f;

            // The player's own line first - the yardstick every rival row reads
            // against, and the boss's face on it: the don looks his rivals in the eye.
            foreach (var gang in gangs)
            {
                if (!gang.IsPlayer)
                    continue;

                DiplomacyMugshot(Gangs.GangCatalog.BossModel,
                    Initials(Gangs.GangCatalog.BossName), ListLeft, y, 64f);
                DiplomacySwatch(gang.Id, ListLeft + 80f, y);
                var you = NewText("You", diplomacyContent, 16f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(you.rectTransform, ListLeft + 104f, y, 640f, 26f);
                you.fontStyle = FontStyles.Bold;
                var held = Outfit.Turf.CountOf(holdings, gang.Id);
                you.text = gang.Name.ToUpperInvariant() + " — YOURS" +
                    (outfit
                        ? "  ·  " + held + " BUILDING" + (held == 1 ? "" : "S")
                        : "");

                var boss = NewText("Boss", diplomacyContent, 13f,
                    LedgerPalette.PhosphorDim, TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(boss.rectTransform, ListLeft + 104f, y - 26f, 400f, 20f);
                boss.text = "Boss: " + Gangs.GangCatalog.BossName;
                y -= 92f;
            }

            foreach (var gang in gangs)
            {
                if (gang.IsPlayer)
                    continue;
                y = DiplomacyRow(gang, y);
            }

            // The legend, under the families - the page must never be the opaque system.
            var legend = NewParagraph("Legend", diplomacyContent, 13f,
                LedgerPalette.PhosphorDim);
            PlaceTopLeft(legend.rectTransform, ListLeft, y - 10f,
                PageRight - ListLeft, 420f);
            legend.text = ":: WHAT A STANCE DOES\n\n" +
                LedgerText.StanceEffect(Outfit.Stance.Peace) + "\n\n" +
                LedgerText.StanceEffect(Outfit.Stance.Truce) + "\n\n" +
                LedgerText.StanceEffect(Outfit.Stance.War) + "\n\n" +
                LedgerText.StanceTakesEffect + "\n\n" +
                "Strength reads UNKNOWN until you have eyes inside a family - " +
                "reconnaissance is work, not a birthright. Their turf shows on the " +
                "map in their colour; the streets are not a secret.";
        }

        float DiplomacyRow(Gangs.Gang gang, float y)
        {
            // The wash first, so the row's furniture prints over it - each family
            // sits on its own faint pane instead of floating in the dark.
            var wash = NewRect("Wash", diplomacyContent);
            PlaceTopLeft(wash, ListLeft - 10f, y + 6f, PageRight - ListLeft + 20f, 94f);
            var washImage = wash.gameObject.AddComponent<Image>();
            washImage.sprite = null;
            washImage.color = LedgerPalette.CardTint;
            washImage.raycastTarget = false;

            // The face of the family: its capo, wearing the model his soldiers
            // answer to on the street.
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : "";
            DiplomacyMugshot(Gangs.GangCatalog.LieutenantModels[gang.Id],
                Initials(leader.Length > 0 ? leader : gang.Name), ListLeft, y - 2f, 72f);

            DiplomacySwatch(gang.Id, ListLeft + 88f, y);
            var name = NewText("Name", diplomacyContent, 16f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(name.rectTransform, ListLeft + 112f, y, 320f, 26f);
            name.fontStyle = FontStyles.Bold;
            name.text = gang.Name.ToUpperInvariant();

            var capo = NewText("Capo", diplomacyContent, 13f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(capo.rectTransform, ListLeft + 112f, y - 25f, 320f, 20f);
            capo.text = leader.Length > 0 ? "Run by " + leader : "Run by persons unknown";

            var front = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
            var frontText = NewText("Front", diplomacyContent, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(frontText.rectTransform, ListLeft + 112f, y - 43f, 320f, 20f);
            frontText.text = front
                ? "Front: " + front.BusinessName
                : "Front: unknown";

            var turf = NewText("Turf", diplomacyContent, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(turf.rectTransform, ListLeft + 112f, y - 61f, 320f, 20f);
            var held = Outfit.Turf.CountOf(holdings, gang.Id);
            turf.text = "Strength: " + LedgerText.StrengthUnknown + "  ·  " +
                (outfit
                    ? "Territory: " + held + (held == 1 ? " building" : " buildings")
                    : "Territory: unknown");

            var current = outfit ? outfit.Relations.StanceWith(gang.Id) : Outfit.Stance.Peace;
            var pending = Outfit.Stance.Peace;
            var hasPending = outfit && outfit.Relations.TryGetPending(gang.Id, out pending);

            var stanceText = NewText("Stance", diplomacyContent, 13f,
                hasPending ? LedgerPalette.Amber : LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(stanceText.rectTransform, ListLeft + 440f, y, 444f, 26f);
            stanceText.fontStyle = FontStyles.Bold;
            stanceText.text = "STANCE: " + LedgerText.StanceLabel(current).ToUpperInvariant() +
                (hasPending
                    ? "  >  " + LedgerText.StanceLabel(pending).ToUpperInvariant() +
                      " FROM NEXT WEEK"
                    : "");

            var effective = hasPending ? pending : current;
            for (var s = 0; s < 3; s++)
            {
                var stance = (Outfit.Stance)s;
                var label = LedgerText.StanceLabel(stance).ToUpperInvariant();
                var chosen = stance == effective;
                var gangId = gang.Id;
                NewButton(diplomacyContent, chosen ? "= " + label + " =" : label,
                    ListLeft + 440f + s * 132f, y - 34f, 124f, 28f, () =>
                    {
                        if (outfit)
                            outfit.SetStance(gangId, stance);
                        dirty = true;
                    });
            }

            var hairline = NewRect("Rule", diplomacyContent);
            PlaceTopLeft(hairline, ListLeft, y - 84f, PageRight - ListLeft, 1f);
            var hairImage = hairline.gameObject.AddComponent<Image>();
            hairImage.sprite = null;
            hairImage.color = LedgerPalette.HairLine;
            hairImage.raycastTarget = false;

            return y - 100f;
        }

        /// <summary>A framed face slot for the diplomacy page - the personnel card's
        /// mugshot recipe at row size. Initials are the placeholder AND the fallback:
        /// PortraitStudio's print covers them when it lands, and when no model
        /// resolves they simply stay.</summary>
        void DiplomacyMugshot(string model, string initials, float x, float y, float size)
        {
            var photo = NewRect("Photo", diplomacyContent);
            PlaceTopLeft(photo, x, y, size, size);
            var photoImage = photo.gameObject.AddComponent<Image>();
            photoImage.sprite = null;
            photoImage.color = LedgerPalette.PhotoBack;
            photoImage.raycastTarget = false;
            Frame(photo, 1f, LedgerPalette.PhosphorDim);

            var text = NewText("Initials", photo, size * 0.32f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.fontStyle = FontStyles.Bold;
            text.text = initials;

            var mugshot = NewRect("Mugshot", photo);
            mugshot.anchorMin = Vector2.zero;
            mugshot.anchorMax = Vector2.one;
            mugshot.offsetMin = mugshot.offsetMax = Vector2.zero;
            var raw = mugshot.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false; // Show() flips it on when the print lands
            PortraitStudio.Request(PortraitStudio.FindPeoplePrefab(model),
                PortraitStudio.Framing.Bust, raw);
        }

        /// <summary>First letters of the first and last word of a name - "Don
        /// Salvatore Ricci" prints DR in the slot until his photograph arrives.</summary>
        static string Initials(string fullName)
        {
            var parts = fullName.Split(' ');
            var head = parts.Length > 0 && parts[0].Length > 0
                ? parts[0][0].ToString() : "";
            var tail = parts.Length > 1 && parts[parts.Length - 1].Length > 0
                ? parts[parts.Length - 1][0].ToString() : "";
            return head + tail;
        }

        void DiplomacySwatch(int gangId, float x, float y)
        {
            var swatch = NewRect("Swatch", diplomacyContent);
            swatch.anchorMin = new Vector2(0f, 1f);
            swatch.anchorMax = new Vector2(0f, 1f);
            swatch.pivot = new Vector2(0f, 1f);
            swatch.anchoredPosition = new Vector2(x, y - 5f);
            swatch.sizeDelta = new Vector2(16f, 16f);
            var image = swatch.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = GangPalette.Of(gangId);
            image.raycastTarget = false;
        }

        // --------------------------------------------------------------- the orders page

        /// <summary>Inner content width of the orders panel - the panel itself now
        /// fills the page below the tabs, since the map lives beside the book.</summary>
        const float OrdersInner = PageRight - ListLeft - 28f;

        RectTransform ordersViewport;
        RectTransform ordersContent;
        float ordersScrollY;

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

        void BuildOrdersPage(RectTransform pageRect)
        {
            var root = NewPageRoot(pageRect, LedgerPage.Orders);

            // A page like any other now: the panel fills the sheet below the tab strip,
            // and target picking happens on the map already standing to the right.
            // Fixed page width, stretched vertically from under the tab strip to a
            // bottom margin: anchored to the paper's left-top column like every sheet.
            var panel = NewRect("Panel", root);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(ListLeft, PageTop - 8f);
            panel.sizeDelta = new Vector2(PageRight - ListLeft, PageTop - 8f - 36f);

            var back = panel.gameObject.AddComponent<Image>();
            back.sprite = null;
            back.color = new Color(LedgerPalette.Screen.r, LedgerPalette.Screen.g,
                LedgerPalette.Screen.b, 0.94f);
            back.raycastTarget = true;
            if (!LedgerSkinSet.TryDressPanel(back) &&
                !UiSkin.TryDress(back, UiSkin.PanelDark))
                Frame(panel, 1f, LedgerPalette.PhosphorDim);

            var title = NewText("Title", panel, 17f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(title.rectTransform, 14f, -10f, 260f, 30f);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 3f;
            title.text = "ORDERS";

            ordersViewport = NewRect("Viewport", panel);
            ordersViewport.anchorMin = new Vector2(0f, 0f);
            ordersViewport.anchorMax = new Vector2(1f, 1f);
            ordersViewport.offsetMin = new Vector2(2f, 2f);
            ordersViewport.offsetMax = new Vector2(-2f, -48f);
            ordersViewport.gameObject.AddComponent<RectMask2D>();

            ordersContent = NewRect("Content", ordersViewport);
            ordersContent.anchorMin = new Vector2(0f, 1f);
            ordersContent.anchorMax = new Vector2(1f, 1f);
            ordersContent.pivot = new Vector2(0f, 1f);
            ordersContent.anchoredPosition = Vector2.zero;
            ordersContent.sizeDelta = new Vector2(0f, 400f);
        }

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
            var color = LedgerPalette.Amber;
            color.a = 0.3f;

            var plan = outfit ? outfit.Plan : null;
            Outfit.PlannedOrder selected = null;
            if (plan != null && selectedOrderId >= 0)
                foreach (var order in plan.Confirmed)
                    if (order.Id == selectedOrderId)
                        selected = order;

            if (selected != null)
            {
                // A confirmed order's targets read in phosphor - the amber wash is
                // reserved for the still-unconfirmed draft. The two states must never
                // look alike.
                color = LedgerPalette.Phosphor;
                color.a = 0.28f;
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

            var y = -8f;

            var week = NewText("Week", ordersContent, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(week.rectTransform, 14f, y, OrdersInner, 20f);
            week.text = outfit.TryGetHeadquarters(out _, out var hqBlock)
                ? "WEEK " + outfit.Campaign.Week + "  ·  HQ at block #" + hqBlock
                : "WEEK " + outfit.Campaign.Week +
                  "  ·  the families are still settling in";
            y -= 26f;

            if (ordersNote.Length > 0)
            {
                var note = NewText("Note", ordersContent, 12.5f, LedgerPalette.Amber,
                    TextAlignmentOptions.TopLeft);
                PlaceTopLeft(note.rectTransform, 14f, y, OrdersInner, 34f);
                note.textWrappingMode = TextWrappingModes.Normal;
                note.text = ordersNote;
                y -= 38f;
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
            ordersScrollY = Mathf.Clamp(ordersScrollY, 0f, maxScroll);
            ordersContent.anchoredPosition = new Vector2(0f, ordersScrollY);

            PushHighlights();
        }

        float OrdersCrewList(Roster roster, float y)
        {
            y = OrdersHeader(":: LIEUTENANTS", y);

            if (roster.Crews.Count == 0)
            {
                var none = NewText("None", ordersContent, 13f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(none.rectTransform, 14f, y, OrdersInner, 20f);
                none.text = "Nobody runs a crew. Promote a man on the PERSONNEL page.";
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

                var label = NewButton(ordersContent,
                    (chosen ? "= " : "") +
                    (lieutenant != null ? lieutenant.Surname.ToUpperInvariant() : "?") +
                    " — " + men + " MEN" + (hasVehicle ? " — CAR" : " — ON FOOT") +
                    (chosen ? " =" : ""),
                    14f, y, OrdersInner, 28f, () =>
                    {
                        ordersCrewId = crewId;
                        selectedOrderId = -1;
                        draftBlocks.Clear();
                        draftLabel = "";
                        ordersNote = "";
                        RefreshTargeting();
                        dirty = true;
                    });
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(10f, 0f, 0f, 0f);
                y -= 30f;

                // The fill indicator: the week's labour, spent left to right.
                var barBack = NewRect("BarBack", ordersContent);
                PlaceTopLeft(barBack, 14f, y, OrdersInner, 6f);
                var backImage = barBack.gameObject.AddComponent<Image>();
                backImage.sprite = null;
                backImage.color = LedgerPalette.PhosphorFaint;
                backImage.raycastTarget = false;
                LedgerSkinSet.TryDressBar(backImage, 6f);

                var fillFraction = men > 0 ? Mathf.Min(1f, committed / (float)men) : 0f;
                var fill = NewRect("Fill", barBack);
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(0f, 1f);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.anchoredPosition = Vector2.zero;
                fill.sizeDelta = new Vector2(OrdersInner * fillFraction, 0f);
                var fillImage = fill.gameObject.AddComponent<Image>();
                fillImage.sprite = null;
                fillImage.color = committed > men ? LedgerPalette.Amber
                    : LedgerPalette.Phosphor;
                fillImage.raycastTarget = false;
                LedgerSkinSet.TryDressBar(fillImage, 6f);

                var committedText = NewText("Committed", ordersContent, 11.5f,
                    committed > men ? LedgerPalette.Amber : LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(committedText.rectTransform, 14f, y - 8f, OrdersInner, 16f);
                committedText.text = LedgerText.CommittedLine(committed, men);
                y -= 30f;
            }

            return y - 6f;
        }

        float OrdersJobCard(Roster roster, float y)
        {
            var crew = roster.FindCrew(ordersCrewId);
            if (crew == null)
            {
                ordersCrewId = -1;
                return y;
            }

            y = OrdersHeader(":: THE JOB", y);

            var spec = CurrentDraftSpec();

            // Category and type cyclers - the whole order table, four buttons.
            NewButton(ordersContent, "<", 14f, y, 28f, 24f, () =>
            {
                ordersCategoryIndex = (ordersCategoryIndex + 4) % 5;
                ordersTypeIndex = 0;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            var categoryText = NewText("Category", ordersContent, 12.5f,
                LedgerPalette.PhosphorDim, TextAlignmentOptions.Center);
            PlaceTopLeft(categoryText.rectTransform, 46f, y, OrdersInner - 92f, 24f);
            categoryText.text =
                LedgerText.CategoryLabel((Outfit.OrderCategory)ordersCategoryIndex)
                    .ToUpperInvariant();
            NewButton(ordersContent, ">", 14f + OrdersInner - 28f, y, 28f, 24f, () =>
            {
                ordersCategoryIndex = (ordersCategoryIndex + 1) % 5;
                ordersTypeIndex = 0;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            y -= 28f;

            NewButton(ordersContent, "<", 14f, y, 28f, 26f, () =>
            {
                ordersTypeIndex = (ordersTypeIndex + categorySpecs.Count - 1)
                    % categorySpecs.Count;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            var typeText = NewText("Type", ordersContent, 15f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            PlaceTopLeft(typeText.rectTransform, 46f, y, OrdersInner - 92f, 26f);
            typeText.fontStyle = FontStyles.Bold;
            typeText.text = LedgerText.OrderLabel(spec.Type).ToUpperInvariant();
            NewButton(ordersContent, ">", 14f + OrdersInner - 28f, y, 28f, 26f, () =>
            {
                ordersTypeIndex = (ordersTypeIndex + 1) % categorySpecs.Count;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            y -= 30f;

            var requirement = NewText("Req", ordersContent, 12f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(requirement.rectTransform, 14f, y, OrdersInner, 18f);
            requirement.text = LedgerText.RequirementLine(
                spec.PrimaryAttribute, spec.PrimaryFloorHalfSteps);
            y -= 20f;

            if (spec.PrimaryFloorHalfSteps > 0)
            {
                var best = Outfit.CrewKit.BestAt(roster, crew, spec.PrimaryAttribute);
                if (best < spec.PrimaryFloorHalfSteps)
                {
                    var warn = NewText("Warn", ordersContent, 12f, LedgerPalette.Amber,
                        TextAlignmentOptions.MidlineLeft);
                    PlaceTopLeft(warn.rectTransform, 14f, y, OrdersInner, 18f);
                    warn.text = "Best man has " + LedgerText.Stars(best) +
                                " - they can try anyway.";
                    y -= 20f;
                }
            }

            var hint = NewText("Hint", ordersContent, 12f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(hint.rectTransform, 14f, y, OrdersInner, 32f);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.text = LedgerText.TargetModeHint(spec.Mode);
            y -= 36f;

            var targetCount = spec.Mode == Outfit.TargetMode.Area
                ? draftBlocks.Count
                : (draftLabel.Length > 0 ? 1 : 0);

            var targets = NewText("Targets", ordersContent, 13f,
                targetCount > 0 ? LedgerPalette.Amber : LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(targets.rectTransform, 14f, y, OrdersInner - 100f, 20f);
            targets.fontStyle = targetCount > 0 ? FontStyles.Bold : FontStyles.Normal;
            targets.text = targetCount == 0
                ? "TARGETS: none yet - UNCONFIRMED"
                : "TARGETS: " + (spec.Mode == Outfit.TargetMode.Area
                    ? draftBlocks.Count + " blocks"
                    : draftLabel) + " - UNCONFIRMED";
            if (targetCount > 0)
                NewButton(ordersContent, "CLEAR", 14f + OrdersInner - 92f, y + 1f,
                    92f, 22f, () =>
                    {
                        draftBlocks.Clear();
                        draftLabel = "";
                        ordersNote = "";
                        dirty = true;
                    });
            y -= 24f;

            if (targetCount > 0)
            {
                var hasVehicle = Outfit.CrewKit.HasVehicle(roster, crew);
                var distance = DraftDistance();
                var travel = Outfit.OrderMath.TravelFraction(distance, hasVehicle);
                var needed = Outfit.OrderMath.MenNeeded(spec, targetCount, travel);

                var travelText = NewText("Travel", ordersContent, 12f,
                    travel > 0.5f ? LedgerPalette.Amber : LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(travelText.rectTransform, 14f, y, OrdersInner, 18f);
                travelText.text = "Travel: " + Mathf.RoundToInt(distance) + "m from HQ " +
                    (hasVehicle ? "by car" : "ON FOOT") + " - eats " +
                    Mathf.RoundToInt(travel * 100f) + "% of each man's week.";
                y -= 20f;

                var neededText = NewText("Needed", ordersContent, 12f,
                    LedgerPalette.PhosphorDim, TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(neededText.rectTransform, 14f, y, OrdersInner, 18f);
                neededText.text = "Needs about " + needed + " man-week" +
                    (needed == 1 ? "" : "s") + " to finish.";
                y -= 24f;

                NewButton(ordersContent, "-", 14f, y, 28f, 26f, () =>
                {
                    if (draftMen > 1)
                    {
                        draftMen--;
                        dirty = true;
                    }
                });
                var menText = NewText("Men", ordersContent, 14f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.Center);
                PlaceTopLeft(menText.rectTransform, 46f, y, 120f, 26f);
                menText.fontStyle = FontStyles.Bold;
                menText.text = "MEN: " + draftMen;
                NewButton(ordersContent, "+", 170f, y, 28f, 26f, () =>
                {
                    draftMen++;
                    dirty = true;
                });

                if (Outfit.OrderMath.Undermanned(spec, targetCount, travel, draftMen))
                {
                    var short_ = NewText("Short", ordersContent, 12f, LedgerPalette.Amber,
                        TextAlignmentOptions.MidlineLeft);
                    PlaceTopLeft(short_.rectTransform, 210f, y, OrdersInner - 200f, 26f);
                    short_.text = "Won't finish this week.";
                }
                y -= 32f;

                var crewId = crew.Id;
                var confirmSpec = spec;
                NewButton(ordersContent, "CONFIRM ORDER", 14f, y, OrdersInner, 32f,
                    () =>
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
                y -= 40f;
            }

            return y - 6f;
        }

        float OrdersThisWeek(Roster roster, float y)
        {
            y = OrdersHeader(":: THIS WEEK", y);
            var plan = outfit.Plan;

            if (plan.Confirmed.Count == 0)
            {
                var none = NewText("None", ordersContent, 12.5f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(none.rectTransform, 14f, y, OrdersInner, 18f);
                none.text = "No orders in the queue.";
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

                var row = NewButton(ordersContent,
                    (chosen ? "= " : "") +
                    (lieutenant != null ? lieutenant.Surname.ToUpperInvariant() : "?") +
                    " · " + LedgerText.OrderLabel(order.Type) + " · " +
                    (order.BlockTargets.Count > 0
                        ? order.BlockTargets.Count + " blk"
                        : order.TargetLabel) +
                    " · " + order.Men + " men" +
                    (past ? "  — PAST THE LINE" : ""),
                    14f, y, OrdersInner - 100f, 26f, () =>
                    {
                        selectedOrderId = chosen ? -1 : orderId;
                        dirty = true;
                    });
                row.alignment = TextAlignmentOptions.MidlineLeft;
                row.margin = new Vector4(8f, 0f, 0f, 0f);
                row.fontSize = 12f;
                if (past)
                    row.color = LedgerPalette.Amber;

                NewButton(ordersContent, "^", 14f + OrdersInner - 96f, y, 26f, 26f,
                    () => { outfit.MoveOrder(orderId, -1); dirty = true; });
                NewButton(ordersContent, "v", 14f + OrdersInner - 66f, y, 26f, 26f,
                    () => { outfit.MoveOrder(orderId, 1); dirty = true; });
                NewButton(ordersContent, "X", 14f + OrdersInner - 36f, y, 26f, 26f,
                    () =>
                    {
                        outfit.RemoveOrder(orderId);
                        if (selectedOrderId == orderId)
                            selectedOrderId = -1;
                        dirty = true;
                    });
                y -= 30f;
            }

            return y - 6f;
        }

        float OrdersLastWeek(float y)
        {
            y = OrdersHeader(":: LAST WEEK", y);

            if (outfit.LastWeek.Count == 0)
            {
                var none = NewText("None", ordersContent, 12.5f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(none.rectTransform, 14f, y, OrdersInner, 18f);
                none.text = "No record yet - the first week is still open.";
                return y - 26f;
            }

            foreach (var record in outfit.LastWeek)
            {
                var color = record.Outcome switch
                {
                    Outfit.OrderOutcome.Completed => LedgerPalette.Phosphor,
                    Outfit.OrderOutcome.Failed => LedgerPalette.Amber,
                    _ => LedgerPalette.Disabled,
                };
                var row = NewText("Record", ordersContent, 12f, color,
                    TextAlignmentOptions.MidlineLeft);
                PlaceTopLeft(row.rectTransform, 14f, y, OrdersInner, 18f);
                row.text = record.Lieutenant + " · " + LedgerText.OrderLabel(record.Type) +
                    " · " + record.TargetSummary + " · " + record.Men + " men — " +
                    LedgerText.OutcomeLabel(record.Outcome).ToUpperInvariant();
                y -= 22f;
            }

            return y - 6f;
        }

        float OrdersCommit(float y)
        {
            y -= 8f;
            if (pendingCommit)
            {
                var warn = NewText("Warn", ordersContent, 12.5f, LedgerPalette.Amber,
                    TextAlignmentOptions.TopLeft);
                PlaceTopLeft(warn.rectTransform, 14f, y, OrdersInner, 36f);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.text = "End planning? Wages fall due, stances turn, and the week " +
                            "runs as ordered.";
                y -= 40f;

                NewButton(ordersContent, "COMMIT", 14f, y, 200f, 32f, () =>
                {
                    pendingCommit = false;
                    selectedOrderId = -1;
                    outfit.CommitWeek();
                    ordersNote = "The week is committed.";
                    dirty = true;
                });
                NewButton(ordersContent, "CANCEL", 228f, y, 160f, 32f, () =>
                {
                    pendingCommit = false;
                    dirty = true;
                });
            }
            else
                NewButton(ordersContent, "COMMIT THE WEEK", 14f, y, OrdersInner, 32f,
                    () =>
                    {
                        pendingCommit = true;
                        dirty = true;
                    });

            return y - 40f;
        }

        float OrdersHeader(string label, float y)
        {
            y -= 6f;
            var header = NewText("Header", ordersContent, 12.5f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(header.rectTransform, 14f, y, OrdersInner, 20f);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 2f;
            header.text = label;
            return y - 24f;
        }

        void BuildScanlines(RectTransform paper)
        {
            // The raster was the CRT's texture; the Modern Menus book has the pack's
            // own chrome for texture and draws none.
            if (LedgerSkinSet.Active)
                return;

            var root = NewRect("Scanlines", paper);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            // Full-width lines hung from the top, laid past the tallest screen the
            // Expand scaler can produce - the paper's mask clips the overshoot.
            for (var y = 3f; y < 1300f; y += 6f)
            {
                var line = NewRect("Scan", root);
                line.anchorMin = new Vector2(0f, 1f);
                line.anchorMax = new Vector2(1f, 1f);
                line.pivot = new Vector2(0f, 1f);
                line.anchoredPosition = new Vector2(0f, -y);
                line.sizeDelta = new Vector2(0f, 1f);
                var image = line.gameObject.AddComponent<Image>();
                image.sprite = null;
                image.color = LedgerPalette.ScanLine;
                image.raycastTarget = false;
            }
        }

        void BuildTitleBar(RectTransform paper)
        {
            // The topbar's floor: the pack's menu bar, laid full-width behind the
            // masthead row so the title, count and CLOSE read as one fixture.
            var band = LedgerSkinSet.Masthead;
            if (band != null)
            {
                var bandRect = NewRect("MastheadBand", paper);
                bandRect.anchorMin = new Vector2(0f, 1f);
                bandRect.anchorMax = new Vector2(1f, 1f);
                bandRect.pivot = new Vector2(0.5f, 1f);
                bandRect.anchoredPosition = Vector2.zero;
                bandRect.sizeDelta = new Vector2(0f, 66f);
                var bandImage = bandRect.gameObject.AddComponent<Image>();
                bandImage.sprite = band;
                bandImage.type = Image.Type.Sliced;
                bandImage.pixelsPerUnitMultiplier = 4f;
                bandImage.color = new Color(LedgerSkinSet.FaceTint.r,
                    LedgerSkinSet.FaceTint.g, LedgerSkinSet.FaceTint.b, 0.4f);
                bandImage.raycastTarget = false;
            }

            titleText = NewText("Title", paper, 22f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(titleText.rectTransform, ListLeft, -14f, 560f, 46f);
            titleText.fontStyle = FontStyles.Bold;
            titleText.characterSpacing = 3f;
            LedgerSkinSet.ApplyHeadline(titleText);
            // The date is written by UpdateBarLabels from the campaign calendar - the
            // year was a hard-coded literal here once, and only once.

            titleCount = NewText("Count", paper, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(titleCount.rectTransform, PageRight - 440f, -20f, 300f, 36f);
            titleCount.characterSpacing = 2f;

            NewToolbarButton(paper, "CLOSE", PageRight - 120f, -18f, 120f, 32f, Close);
        }

        void BuildFilterBar(RectTransform parent)
        {
            // One packed toolbar. The chip's sprite carries ~7 units of transparent
            // margin (12+16px at PPU x4), so each segment overlaps the last by that
            // much - the visible edges kiss and the row reads as one bar.
            sortLabel = NewToolbarButton(parent, "", ListLeft, PageTop, 240f, 32f,
                ToggleSortMenu);
            rankLabel = NewToolbarButton(parent, "", ListLeft + 233f, PageTop, 200f, 32f,
                CycleRank);
            postLabel = NewToolbarButton(parent, "", ListLeft + 426f, PageTop, 200f, 32f,
                CyclePost);
            showLabel = NewToolbarButton(parent, "", ListLeft + 619f, PageTop, 200f, 32f,
                CycleShow);
            UpdateBarLabels();
        }

        /// <summary>Thirteen fixed rows, built once; it toggles rather than rebuilds
        /// because its contents never change. Built LAST under the paper so hierarchy
        /// order draws it over the list.</summary>
        void BuildSortMenu(RectTransform paper)
        {
            const float rowH = 28f;
            var entries = 2 + AttributeScale.Count;

            sortMenu = new GameObject("SortMenu", typeof(RectTransform));
            sortMenu.transform.SetParent(paper, false);
            var rect = (RectTransform)sortMenu.transform;
            PlaceTopLeft(rect, ListLeft, PageTop - 38f, 360f, entries * rowH + 8f);

            var back = sortMenu.AddComponent<Image>();
            back.sprite = null;
            back.color = LedgerPalette.Screen;
            back.raycastTarget = true; // The menu's own body must swallow stray clicks.
            if (!LedgerSkinSet.TryDressPanel(back) &&
                !UiSkin.TryDress(back, UiSkin.PanelDark))
                Frame(rect, 1f, LedgerPalette.PhosphorDim);

            for (var i = 0; i < entries; i++)
            {
                var index = i;
                string label;
                if (i == 0)
                    label = "ROSTER ORDER";
                else if (i <= AttributeScale.Count)
                    label = LedgerText.AttributeLabel((CharacterAttribute)(i - 1))
                        .ToUpperInvariant();
                else
                    label = "LOYALTY";

                var button = NewToolbarButton(rect, label, 4f, -4f - i * rowH, 352f,
                    rowH, () =>
                {
                    if (index == 0)
                        options.Sort = SortKey.Roster;
                    else if (index <= AttributeScale.Count)
                    {
                        options.Sort = SortKey.Attribute;
                        options.SortAttribute = (CharacterAttribute)(index - 1);
                    }
                    else
                        options.Sort = SortKey.Loyalty;

                    sortMenu.SetActive(false);
                    dirty = true;
                });
                button.alignment = TextAlignmentOptions.MidlineLeft;
                button.margin = new Vector4(10f, 0f, 0f, 0f);
            }

            sortMenu.SetActive(false);
        }

        void ToggleSortMenu()
        {
            if (sortMenu)
                sortMenu.SetActive(!sortMenu.activeSelf);
        }

        void CycleRank()
        {
            options.Rank = (RankFilter)(((int)options.Rank + 1) % 4);
            dirty = true;
        }

        void CyclePost()
        {
            options.Assignment = (AssignmentFilter)(((int)options.Assignment + 1) % 4);
            dirty = true;
        }

        void CycleShow()
        {
            options.Availability = (AvailabilityFilter)(((int)options.Availability + 1) % 3);
            dirty = true;
        }

        void UpdateBarLabels()
        {
            if (!sortLabel)
                return;

            sortLabel.text = options.Sort switch
            {
                SortKey.Attribute => "SORT: " +
                    LedgerText.AttributeLabel(options.SortAttribute).ToUpperInvariant(),
                SortKey.Loyalty => "SORT: LOYALTY",
                _ => "SORT: ROSTER ORDER",
            };
            rankLabel.text = options.Rank switch
            {
                RankFilter.Hoods => "RANK: HOODS",
                RankFilter.Lieutenants => "RANK: LIEUTENANTS",
                RankFilter.Specialists => "RANK: SPECIALISTS",
                _ => "RANK: ALL",
            };
            postLabel.text = options.Assignment switch
            {
                AssignmentFilter.Crews => "POST: CREWS",
                AssignmentFilter.Pool => "POST: POOL",
                AssignmentFilter.Front => "POST: FRONT",
                _ => "POST: ALL",
            };
            showLabel.text = options.Availability switch
            {
                AvailabilityFilter.ActiveOnly => "SHOW: ACTIVE",
                AvailabilityFilter.Unavailable => "SHOW: OUT OF ACTION",
                _ => "SHOW: ALL",
            };

            var weekOfYear = outfit ? outfit.Campaign.WeekOfYear : 1;
            var year = outfit ? outfit.Campaign.Year : Outfit.Campaign.StartYear;

            if (titleCount)
                titleCount.text = currentPage == LedgerPage.Personnel &&
                                  director.Roster != null
                    ? LedgerText.MemberCount(director.Roster.Members.Count)
                    : "";

            if (titleText)
                titleText.SetText("OUTFIT LEDGER  //  WEEK {0}, {1}", weekOfYear, year);

            if (newspaperDateline)
                newspaperDateline.SetText(
                    "WEEK {0}, {1}  —  MORNING EDITION  —  10 CENTS", weekOfYear, year);
        }

        // ------------------------------------------------------------------ the list

        void RebuildList()
        {
            foreach (Transform old in listContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null)
                return;

            // Assign mode reads the whole book: filters fall away (a valid target must
            // never be hidden by one) and the empty sections show as targets.
            var effective = options;
            if (assignMode)
            {
                effective.Rank = RankFilter.All;
                effective.Assignment = AssignmentFilter.All;
                effective.Availability = AvailabilityFilter.All;
                effective.IncludeEmptySections = true;
            }

            RosterView.Build(roster, effective, rows);

            var y = 0f;
            var indented = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        indented = true;
                        y -= CrewGap;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        // One indent rule for the whole ledger: the front's and the
                        // pool's men step in exactly as far as a crew's hoods do.
                        indented = true;
                        BuildSectionHeader(row.Kind, y);
                        y -= SectionHeaderHeight;
                        break;

                    case RowKind.Lieutenant:
                        BuildCharacterRow(roster, row.CharacterId, y, indent: false,
                            lieutenantRow: true);
                        y -= RowHeight;
                        break;

                    default:
                        BuildCharacterRow(roster, row.CharacterId, y, indented);
                        y -= RowHeight;
                        break;
                }
            }

            listContent.sizeDelta = new Vector2(0f, Mathf.Max(ListHeight, -y + 8f));
            var maxScroll = Mathf.Max(0f, listContent.sizeDelta.y - ListHeight);
            scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);
            listContent.anchoredPosition = new Vector2(0f, scrollY);
        }

        void BuildSectionHeader(RowKind kind, float y)
        {
            var rect = NewRect("Section", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, SectionHeaderHeight);

            var isTarget = assignMode && selectedId >= 0 && kind != RowKind.SpecialistHeader;
            // The front's header is also the BOSS's row: clicking it opens the front
            // card - his face, the desk, the locker - the way a member row opens his.
            var frontSelectable = kind == RowKind.FrontHeader && !assignMode;
            var inverse = frontSelectable && selectedId == FrontSelection;

            if (isTarget || frontSelectable)
            {
                var background = rect.gameObject.AddComponent<Image>();
                background.sprite = null;
                background.color = isTarget ? LedgerPalette.Target
                    : inverse ? LedgerPalette.Phosphor
                    : Color.clear;
                background.raycastTarget = true;

                if (isTarget)
                {
                    var toPool = kind == RowKind.PoolHeader;
                    AddRowButton(rect, background, () => FinishAssign(toPool
                        ? director.AssignToPool(selectedId)
                        : director.AssignToFront(selectedId)));
                }
                else
                    AddRowButton(rect, background, () => SelectMember(FrontSelection));
            }

            // THE FRONT and THE POOL head groups of men the same way a lieutenant
            // heads his crew, so they wear his exact dress: the full-beam pip, the
            // bold 15pt name, the hairline underneath - no "==" decoration.
            var crewStyle = kind != RowKind.SpecialistHeader;
            if (crewStyle)
            {
                var hairline = NewRect("Hairline", rect);
                hairline.anchorMin = new Vector2(0f, 0f);
                hairline.anchorMax = new Vector2(1f, 0f);
                hairline.pivot = new Vector2(0f, 0f);
                hairline.anchoredPosition = Vector2.zero;
                hairline.sizeDelta = new Vector2(0f, 1f);
                var hairImage = hairline.gameObject.AddComponent<Image>();
                hairImage.sprite = null;
                hairImage.color = LedgerPalette.HairLine;
                hairImage.raycastTarget = false;

                var pip = NewRect("Pip", rect);
                pip.anchorMin = new Vector2(0f, 0f);
                pip.anchorMax = new Vector2(0f, 1f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.anchoredPosition = Vector2.zero;
                pip.sizeDelta = new Vector2(4f, 0f);
                var pipImage = pip.gameObject.AddComponent<Image>();
                pipImage.sprite = null;
                pipImage.color = inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor;
                pipImage.raycastTarget = false;
            }

            var label = NewText("Label", rect, crewStyle ? 15f : 14f,
                inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            FillRow(label.rectTransform, 12f, 400f);
            label.fontStyle = FontStyles.Bold;
            if (!crewStyle)
                label.characterSpacing = 3f;
            label.text = kind switch
            {
                RowKind.FrontHeader => "THE FRONT",
                RowKind.PoolHeader => "THE POOL",
                _ => "== SPECIALISTS ==",
            };
        }

        void BuildCharacterRow(Roster roster, int id, float y, bool indent,
            bool lieutenantRow = false)
        {
            var member = roster.Find(id);
            if (member == null)
                return;

            var rect = NewRect("Row", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, RowHeight);

            var inverse = id == selectedId && !assignMode;
            // A lieutenant's row is his crew's handle: in assign mode it lights as
            // the drop target the crew band used to be.
            var isCrewTarget = assignMode && lieutenantRow && selectedId >= 0;
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            // No zebra stripes - the raster's own scanlines rule this page. The
            // background catches the click, and lights to full phosphor for the
            // selection: inverse video, the terminal's one and only highlight.
            background.color = isCrewTarget ? LedgerPalette.Target
                : inverse ? LedgerPalette.Phosphor
                : Color.clear;
            background.raycastTarget = true;

            var hairline = NewRect("Hairline", rect);
            hairline.anchorMin = new Vector2(0f, 0f);
            hairline.anchorMax = new Vector2(1f, 0f);
            hairline.pivot = new Vector2(0f, 0f);
            hairline.anchoredPosition = Vector2.zero;
            hairline.sizeDelta = new Vector2(0f, 1f);
            var hairImage = hairline.gameObject.AddComponent<Image>();
            hairImage.sprite = null;
            hairImage.color = LedgerPalette.HairLine;
            hairImage.raycastTarget = false;

            // In assign mode the LIEUTENANT'S row takes the man into his crew - the
            // user's rule: the lieutenant IS his group's handle. An ordinary man is
            // no target, so clicking one cancels the mode - the gesture for "never
            // mind" that costs nothing to discover.
            var crew = lieutenantRow ? roster.CrewOf(id) : null;
            var crewId = crew != null ? crew.Id : -1;
            AddRowButton(rect, background, () =>
            {
                if (assignMode)
                {
                    if (crewId >= 0)
                        FinishAssign(director.AssignToCrew(selectedId, crewId));
                    else
                    {
                        assignMode = false;
                        dirty = true;
                    }
                }
                else
                    SelectMember(id);
            });

            if (lieutenantRow)
            {
                // The head of the crew wears a full-beam pip down his row's left edge -
                // rank reads before the name does, even at sixty men.
                var pip = NewRect("Pip", rect);
                pip.anchorMin = new Vector2(0f, 0f);
                pip.anchorMax = new Vector2(0f, 1f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.anchoredPosition = Vector2.zero;
                pip.sizeDelta = new Vector2(4f, 0f);
                var pipImage = pip.gameObject.AddComponent<Image>();
                pipImage.sprite = null;
                pipImage.color = inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor;
                pipImage.raycastTarget = false;
            }

            var x = 12f + (indent ? HoodIndent : 0f);
            var dim = member.Status == CharacterStatus.Dead || assignMode;

            var name = NewText("Name", rect, 15f,
                inverse ? LedgerPalette.Screen
                : dim ? LedgerPalette.Disabled
                : LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            FillRow(name.rectTransform, x, 250f - x);
            if (lieutenantRow)
                name.fontStyle = FontStyles.Bold;
            name.text = member.FullName.ToUpperInvariant();

            BuildRowCells(rect, member, 258f, dim: dim, inverse: inverse);
        }

        /// <summary>The scan columns every character-bearing row shares: rank tag, status
        /// stamp, wanted diamond, items count, and - under an attribute or loyalty sort -
        /// the sorted value itself, right-aligned where a column of numbers can be read
        /// straight down. That column is what makes sixty men scannable. On an inverse
        /// (selected) row every cell goes tube-dark; dim mutes the row instead.</summary>
        void BuildRowCells(RectTransform rect, Character member, float x, bool dim,
            bool inverse)
        {
            var muted = inverse ? LedgerPalette.Screen
                : dim ? LedgerPalette.Disabled
                : LedgerPalette.PhosphorDim;

            var rank = NewText("Rank", rect, 11.5f, muted,
                TextAlignmentOptions.MidlineLeft);
            FillRow(rank.rectTransform, x, 92f);
            rank.text = (member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank)).ToUpperInvariant();

            if (member.Status != CharacterStatus.Active)
            {
                var status = NewText("Status", rect, 10.5f,
                    inverse ? LedgerPalette.Screen
                    : dim ? LedgerPalette.Disabled
                    : LedgerPalette.Amber,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(status.rectTransform, x + 96f, 74f);
                status.fontStyle = FontStyles.Bold;
                status.text = LedgerText.StatusLabel(member.Status).ToUpperInvariant();
            }

            if (member.Wanted)
            {
                var diamond = NewRect("Wanted", rect);
                diamond.anchorMin = new Vector2(0f, 0.5f);
                diamond.anchorMax = new Vector2(0f, 0.5f);
                diamond.pivot = new Vector2(0.5f, 0.5f);
                diamond.anchoredPosition = new Vector2(x + 184f, 0f);
                diamond.sizeDelta = new Vector2(10f, 10f);
                diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var image = diamond.gameObject.AddComponent<Image>();
                image.sprite = null;
                image.color = LedgerPalette.Amber;
                image.raycastTarget = false;
            }

            // The items-count cell is gone with the narrow list: what a man carries
            // reads off his card, one click away.
            if (options.Sort != SortKey.Roster && !assignMode)
            {
                var value = NewText("Value", rect, 15f,
                    inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor,
                    TextAlignmentOptions.MidlineRight);
                FillRow(value.rectTransform, ListWidth - 70f, 62f);
                value.fontStyle = FontStyles.Bold;
                value.text = options.Sort == SortKey.Loyalty
                    ? member.Loyalty.ToString()
                    : LedgerText.Stars(member.GetHalfSteps(options.SortAttribute));
            }
        }

        void SelectMember(int id)
        {
            selectedId = id;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            dirty = true;
        }

        void FinishAssign(OpResult result)
        {
            assignMode = false;
            lastRefusal = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        // ---------------------------------------------------------------- the detail

        /// <summary>The pack faces the mugshots draw from - suits for the men who run
        /// things, street muscle for the men who do them. Verified against the baked
        /// PrefabDatabase pedestrian groups by name.</summary>
        static readonly string[] LieutenantLooks =
        {
            "SM_Chr_Detective_Male_01_AI",
            "SM_Gen_Chr_Business_Male_01_AI",
            "SM_Chr_City_Male_01_AI",
        };

        static readonly string[] HoodLooks =
        {
            "SM_Chr_Gang_Male_01_AI",
            "SM_Chr_Gang_Male_02_AI",
            "SM_Chr_Goon_01_AI",
            "SM_Chr_Criminal_Male_01_AI",
            "SM_Gen_Chr_Street_Male_01_AI",
            "SM_Gen_Chr_Street_Male_02_AI",
            "SM_Chr_City_Male_02_AI",
            "SM_Chr_Salesman_01_AI",
        };

        /// <summary>The face this member wears in his photograph: a fitting pack model
        /// picked by his stable Id - sixty men are not one man in one coat. Specialists
        /// and lieutenants draw from the suits, hoods from the street. No rng: the Id
        /// indexes the table directly, so the same man always sits for the same photo
        /// and no shared stream is disturbed.</summary>
        static GameObject MemberModel(Character member)
        {
            var looks = member.Rank == Rank.Lieutenant ||
                        member.Specialty != Specialty.None
                ? LieutenantLooks : HoodLooks;
            return PortraitStudio.FindPeoplePrefab(looks[member.Id % looks.Length]);
        }

        /// <summary>The wash behind the identity block - the card's own masthead. A
        /// breath of phosphor kept inside the card's padding: the old version bled to
        /// the rect edge and its hairline floor stuck out past the panel sprite's
        /// drawn face, both gone at the user's word. Built FIRST so everything prints
        /// over it.</summary>
        void BuildCardMasthead()
        {
            var band = NewRect("Masthead", detailContent);
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.anchoredPosition = Vector2.zero;
            band.sizeDelta = new Vector2(0f, CardMastheadHeight);
            var bandImage = band.gameObject.AddComponent<Image>();
            bandImage.sprite = null;
            bandImage.color = LedgerPalette.CardMasthead;
            bandImage.raycastTarget = false;
        }

        /// <summary>Prints the shared note just under the hovered row, sized to its
        /// copy. Row coordinates are DetailContent's, inset by CardPad from the card
        /// the note itself hangs on.</summary>
        void ShowHoverNote(string note, RectTransform row)
        {
            if (note.Length == 0 || hoverNote == null)
                return;

            hoverNoteText.text = note;
            hoverNote.gameObject.SetActive(true);
            hoverNote.SetAsLastSibling();

            var height = hoverNoteText.GetPreferredValues(
                note, CardInnerWidth - 20f, 0f).y + 16f;
            hoverNote.sizeDelta = new Vector2(CardInnerWidth, height);
            hoverNote.anchoredPosition = new Vector2(CardPad,
                row.anchoredPosition.y - row.sizeDelta.y - CardPad - 2f);
        }

        void HideHoverNote()
        {
            if (hoverNote != null)
                hoverNote.gameObject.SetActive(false);
        }

        /// <summary>The pointer half of the card's hover notes: an invisible zone laid
        /// over one stat row. AddComponent-only, never serialized, so nesting inside
        /// the almanac is safe.</summary>
        sealed class StatHoverZone : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler
        {
            public PersonnelAlmanac almanac;
            public string note;

            public void OnPointerEnter(PointerEventData eventData) =>
                almanac.ShowHoverNote(note, (RectTransform)transform);

            public void OnPointerExit(PointerEventData eventData) =>
                almanac.HideHoverNote();
        }

        void RebuildDetail()
        {
            // The rows under the pointer are about to be destroyed, and destroyed
            // rows send no PointerExit - drop the note with them.
            HideHoverNote();

            foreach (Transform old in detailContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster != null && selectedId == FrontSelection)
            {
                BuildFrontDetail(roster);
                return;
            }

            var member = roster != null && selectedId >= 0 ? roster.Find(selectedId) : null;
            if (member == null)
            {
                var hint = NewText("Hint", detailContent, 15f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.Center);
                hint.rectTransform.anchorMin = Vector2.zero;
                hint.rectTransform.anchorMax = Vector2.one;
                hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
                hint.text = "Select a man from the roster.";
                return;
            }

            BuildCardMasthead();

            // The mugshot corner. The initials are the placeholder AND the fallback:
            // PortraitStudio photographs the member's street model a frame later and its
            // opaque print covers them; when no model resolves, they simply stay.
            var photo = NewRect("Photo", detailContent);
            PlaceTopLeft(photo, 20f, -18f, 84f, 84f);
            var photoImage = photo.gameObject.AddComponent<Image>();
            photoImage.sprite = null;
            photoImage.color = LedgerPalette.PhotoBack;
            photoImage.raycastTarget = false;
            Frame(photo, 1f, LedgerPalette.PhosphorDim);

            var initials = NewText("Initials", photo, 30f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            initials.rectTransform.anchorMin = Vector2.zero;
            initials.rectTransform.anchorMax = Vector2.one;
            initials.rectTransform.offsetMin = initials.rectTransform.offsetMax = Vector2.zero;
            initials.fontStyle = FontStyles.Bold;
            initials.text = (member.FirstName.Length > 0 ? member.FirstName[0].ToString() : "") +
                            (member.Surname.Length > 0 ? member.Surname[0].ToString() : "");

            var mugshot = NewRect("Mugshot", photo);
            mugshot.anchorMin = Vector2.zero;
            mugshot.anchorMax = Vector2.one;
            mugshot.offsetMin = mugshot.offsetMax = Vector2.zero;
            var mugshotImage = mugshot.gameObject.AddComponent<RawImage>();
            mugshotImage.raycastTarget = false;
            mugshotImage.enabled = false; // Show() flips it on when the print lands
            PortraitStudio.Request(MemberModel(member),
                PortraitStudio.Framing.Bust, mugshotImage);

            var name = NewText("Name", detailContent, 19f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(name.rectTransform, 118f, -20f, CardInnerWidth - 138f, 32f);
            name.fontStyle = FontStyles.Bold;
            name.text = member.FullName;

            var rankLine = member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank);
            var assignment = roster.AssignmentOf(member.Id);
            var crewName = "";
            if (assignment.Kind == AssignmentKind.Crew)
            {
                var crew = roster.FindCrew(assignment.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                crewName = lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname)
                    : "A crew";
            }

            var post = NewText("Post", detailContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(post.rectTransform, 118f, -54f, CardInnerWidth - 138f, 22f);
            post.text = rankLine + "  ·  " +
                LedgerText.AssignmentLine(assignment, crewName);

            var statusColor = member.Status == CharacterStatus.Active
                ? LedgerPalette.PhosphorDim
                : LedgerPalette.Amber;
            var status = NewText("Status", detailContent, 14f, statusColor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(status.rectTransform, 118f, -78f, CardInnerWidth - 138f, 22f);
            status.text = "Status: " + LedgerText.StatusLabel(member.Status) +
                          (member.Wanted ? "  ·  WANTED" : "");

            var y = -118f;

            y = BuildLoyaltyBar(member, y);
            y -= 10f;

            for (var a = 0; a < AttributeScale.Count; a++)
                y = BuildAttributeRow(member, (CharacterAttribute)a, y);
            y -= 10f;

            y = BuildEquipmentSection(roster, member, y);
            y -= 12f;

            BuildActionStrip(roster, member, y);
        }

        /// <summary>The front's card - the BOSS's card: his face and name up top (the
        /// main character finally has a page), then the desk, the guards, what sits
        /// at the front, and the stock with GIVE straight into the headquarters
        /// locker. NormalizeArms deals the locker to the men guarding the desk the
        /// moment gear lands, so the card repaints already-dealt.</summary>
        void BuildFrontDetail(Roster roster)
        {
            BuildCardMasthead();

            var photo = NewRect("Photo", detailContent);
            PlaceTopLeft(photo, 20f, -18f, 84f, 84f);
            var photoImage = photo.gameObject.AddComponent<Image>();
            photoImage.sprite = null;
            photoImage.color = LedgerPalette.PhotoBack;
            photoImage.raycastTarget = false;
            Frame(photo, 1f, LedgerPalette.PhosphorDim);

            var initials = NewText("Initials", photo, 30f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            initials.rectTransform.anchorMin = Vector2.zero;
            initials.rectTransform.anchorMax = Vector2.one;
            initials.rectTransform.offsetMin = initials.rectTransform.offsetMax = Vector2.zero;
            initials.fontStyle = FontStyles.Bold;
            initials.text = "DR";

            var mugshot = NewRect("Mugshot", photo);
            mugshot.anchorMin = Vector2.zero;
            mugshot.anchorMax = Vector2.one;
            mugshot.offsetMin = mugshot.offsetMax = Vector2.zero;
            var mugshotImage = mugshot.gameObject.AddComponent<RawImage>();
            mugshotImage.raycastTarget = false;
            mugshotImage.enabled = false; // Show() flips it on when the print lands
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.BossModel),
                PortraitStudio.Framing.Bust, mugshotImage);

            var name = NewText("Name", detailContent, 19f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(name.rectTransform, 118f, -20f, CardInnerWidth - 138f, 32f);
            name.fontStyle = FontStyles.Bold;
            name.text = Gangs.GangCatalog.BossName;

            var post = NewText("Post", detailContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(post.rectTransform, 118f, -54f, CardInnerWidth - 138f, 22f);
            post.text = "BOSS  ·  " +
                Gangs.GangCatalog.Names[Gangs.GangCatalog.PlayerGangId];

            var y = -118f;

            // The desk and its guard.
            var manager = roster.Find(roster.FrontId);
            y = DetailLine(":: THE DESK", LedgerPalette.PhosphorDim, y);
            y = DetailLine(manager != null
                    ? manager.FullName + " runs the desk."
                    : "Nobody runs the desk.",
                manager != null ? LedgerPalette.Phosphor : LedgerPalette.Amber, y);

            var guards = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Active &&
                    member.Id != roster.FrontId &&
                    roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool)
                    guards++;
            }
            y = DetailLine(guards == 1
                    ? "1 hood on guard at the front."
                    : guards + " hoods on guard at the front.",
                guards > 0 ? LedgerPalette.Phosphor : LedgerPalette.PhosphorDim, y);

            // What the front holds - the locker and the guards' hands.
            y = DetailLine(":: AT THE FRONT", LedgerPalette.PhosphorDim, y - 4f);
            var anyHeld = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.FrontArmory)
                    continue;
                var holder = roster.Find(item.HolderId);
                anyHeld = true;

                y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                    item.DisplayName + "  —  " +
                    (holder != null ? LedgerText.HeldByLine(holder.FullName)
                        : "in the locker"),
                    LedgerPalette.Phosphor, y + 2f);
                var itemId = item.Id;
                NewCardKey(detailContent, "RETURN", CardInnerWidth - 130f, y + 24f,
                    110f, 22f, false, () =>
                    {
                        lastRefusal = "";
                        var result = director.ReturnEquipment(itemId);
                        if (!result.Ok)
                            lastRefusal = result.Reason;
                        dirty = true;
                    });
            }
            if (!anyHeld)
                y = DetailLine("The locker is empty.", LedgerPalette.PhosphorDim, y);

            // The stock: GIVE dumps gear at the front, the guards draw it at once.
            y = DetailLine(":: ARMORY", LedgerPalette.PhosphorDim, y - 4f);
            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.Unheld)
                    continue;
                anyStock = true;

                y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                    item.DisplayName, LedgerPalette.Phosphor, y + 2f);
                var itemId = item.Id;
                NewCardKey(detailContent, "GIVE", CardInnerWidth - 130f, y + 24f,
                    110f, 22f, false, () =>
                    {
                        lastRefusal = "";
                        var result = director.GiveEquipmentToFront(itemId);
                        if (!result.Ok)
                            lastRefusal = result.Reason;
                        dirty = true;
                    });
            }
            if (!anyStock)
                y = DetailLine("The stock is empty.", LedgerPalette.PhosphorDim, y);

            if (lastRefusal.Length > 0)
                DetailLine(lastRefusal, LedgerPalette.Amber, y - 4f);
        }

        float DetailLine(string text, Color color, float y)
        {
            var line = NewText("Line", detailContent, 14f, color,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(line.rectTransform, 20f, y, CardInnerWidth - 40f, 22f);
            line.text = text;
            return y - 24f;
        }

        float BuildLoyaltyBar(Character member, float y)
        {
            var label = NewText("Loyalty", detailContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(label.rectTransform, 20f, y, 110f, 20f);
            label.text = "Loyalty";

            var back = NewRect("Bar", detailContent);
            PlaceTopLeft(back, 140f, y - 4f, 160f, 12f);
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = null;
            backImage.color = LedgerPalette.PhosphorFaint;
            backImage.raycastTarget = false;
            LedgerSkinSet.TryDressBar(backImage, 12f);

            var fill = NewRect("Fill", back);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(160f * (member.Loyalty / 100f), 0f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = null;
            fillImage.color = LedgerPalette.Phosphor;
            fillImage.raycastTarget = false;
            LedgerSkinSet.TryDressBar(fillImage, 12f);

            var value = NewText("Value", detailContent, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(value.rectTransform, CardInnerWidth - 90f, y, 70f, 20f);
            value.text = member.Loyalty.ToString();

            return y - 26f;
        }

        float BuildAttributeRow(Character member, CharacterAttribute attribute, float y)
        {
            var label = NewText("Label", detailContent, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(label.rectTransform, 20f, y, 150f, 22f);
            label.text = LedgerText.AttributeLabel(attribute);

            var halfSteps = member.GetHalfSteps(attribute);
            BuildStarStrip(178f, y - 11f, halfSteps);

            var value = NewText("Value", detailContent, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(value.rectTransform, CardInnerWidth - 90f, y, 70f, 22f);
            value.text = LedgerText.Stars(halfSteps);

            // The whole line is a hover zone: rest the pointer on a stat and the
            // note under it says what the number is FOR.
            var zone = NewRect("Hover", detailContent);
            PlaceTopLeft(zone, 12f, y, CardInnerWidth - 24f, 25f);
            var zoneImage = zone.gameObject.AddComponent<Image>();
            zoneImage.sprite = null;
            zoneImage.color = Color.clear;
            zoneImage.raycastTarget = true;
            var hover = zone.gameObject.AddComponent<StatHoverZone>();
            hover.almanac = this;
            hover.note = LedgerText.AttributeNote(attribute);

            return y - 26f;
        }

        /// <summary>
        /// Five real stars. Skinned, they are the pack's own gold star icon: a full
        /// star shows the sprite as authored, an empty slot is the same sprite ghosted
        /// by StarEmptyTint, and a half is a Filled-horizontal overlay lighting the
        /// left of the ghost - the pack ships ONE star, so the states are tints and a
        /// fill, not sprite swaps. Sprite-less, UiSkin's baked gold family (full /
        /// half / dim empty) carries the pitch exactly as before.
        /// </summary>
        void BuildStarStrip(float x, float centreY, int halfSteps)
        {
            var packStar = LedgerSkinSet.Star;

            for (var slot = 0; slot < 5; slot++)
            {
                var rect = NewRect("Star", detailContent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition =
                    new Vector2(x + slot * StarPitch + StarPitch * 0.5f, centreY);
                rect.sizeDelta = new Vector2(StarSize, StarSize);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;

                if (packStar == null)
                {
                    image.sprite = halfSteps >= (slot + 1) * 2 ? UiSkin.StarFull
                        : halfSteps == slot * 2 + 1 ? UiSkin.StarHalf
                        : UiSkin.StarEmpty;
                    image.color = Color.white;
                    continue;
                }

                image.sprite = packStar;
                image.color = LedgerSkinSet.StarEmptyTint;

                var earnedHalves = halfSteps - slot * 2;
                if (earnedHalves <= 0)
                    continue;

                var lit = NewRect("Lit", rect);
                lit.anchorMin = Vector2.zero;
                lit.anchorMax = Vector2.one;
                lit.offsetMin = lit.offsetMax = Vector2.zero;
                var litImage = lit.gameObject.AddComponent<Image>();
                litImage.sprite = packStar;
                litImage.color = Color.white;
                litImage.raycastTarget = false;
                litImage.type = Image.Type.Filled;
                litImage.fillMethod = Image.FillMethod.Horizontal;
                litImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                litImage.fillAmount = earnedHalves >= 2 ? 1f : 0.5f;
            }
        }

        float BuildEquipmentSection(Roster roster, Character member, float y)
        {
            y = DetailLine(":: IN HAND", LedgerPalette.PhosphorDim, y);

            roster.HeldBy(member.Id, held);
            if (held.Count == 0)
                y = DetailLine("Nothing signed out.", LedgerPalette.PhosphorDim, y);

            for (var i = 0; i < held.Count; i++)
            {
                var item = held[i];
                y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                    item.DisplayName, LedgerPalette.Phosphor, y + 2f);
                NewCardKey(detailContent, "RETURN", CardInnerWidth - 130f, y + 24f,
                    110f, 22f, false, () =>
                    {
                        lastRefusal = "";
                        var result = director.ReturnEquipment(item.Id);
                        if (!result.Ok)
                            lastRefusal = result.Reason;
                        dirty = true;
                    });
            }

            // The armory ledger is a LIEUTENANT's business: gear issues only to a
            // crew's head, so on a hood's or specialist's card the stock listing -
            // header included - is noise. His card ends at what he personally carries.
            if (member.Rank != Rank.Lieutenant)
                return y;

            y = DetailLine(":: ARMORY", LedgerPalette.PhosphorDim, y - 4f);

            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                // His crew's own deck shows through his men's IN HAND lines, not here.
                if (item.OwnerId == member.Id)
                    continue;
                anyStock = true;

                if (item.OwnerId == RosterEquipment.Unheld)
                {
                    y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                        item.DisplayName, LedgerPalette.Phosphor, y + 2f);
                    if (member.Status != CharacterStatus.Dead)
                        NewCardKey(detailContent, "GIVE", CardInnerWidth - 130f, y + 24f,
                            110f, 22f, false, () =>
                            {
                                lastRefusal = "";
                                var result = director.GiveEquipment(item.Id, member.Id);
                                if (!result.Ok)
                                    lastRefusal = result.Reason;
                                dirty = true;
                            });
                }
                else
                {
                    // The finite pool made visible: an item another group owns shows
                    // here, muted, with no button - the only path is returning it,
                    // so exclusivity cannot be violated from the UI at all.
                    var holder = roster.Find(item.HolderId);
                    y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                        item.DisplayName + "  —  " +
                        LedgerText.HeldByLine(holder != null ? holder.FullName
                            : item.OwnerId == RosterEquipment.FrontArmory
                                ? "the front" : "?"),
                        LedgerPalette.Disabled, y + 2f);
                }
            }

            if (!anyStock)
                y = DetailLine("The stock is empty.", LedgerPalette.PhosphorDim, y);

            return y;
        }

        /// <summary>
        /// The card's verbs, or - because there is no dialog system and never has been -
        /// the inline confirm that replaces them: the warning line plus PROMOTE ANYWAY /
        /// CANCEL swap into the same space the PROMOTE button occupied, in the panel the
        /// player is already reading.
        /// </summary>
        void BuildActionStrip(Roster roster, Character member, float y)
        {
            if (lastRefusal.Length > 0)
                y = DetailLine(lastRefusal, LedgerPalette.Amber, y);

            if (member.Status == CharacterStatus.Dead || member.Specialty != Specialty.None)
                return;

            if (pendingConfirm == Confirm.Promote)
            {
                var warn = NewText("Warn", detailContent, 13f, LedgerPalette.Amber,
                    TextAlignmentOptions.TopLeft);
                PlaceTopLeft(warn.rectTransform, 20f, y, CardInnerWidth - 40f, 44f);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.text = LedgerText.PromoteWarning(member.FullName);
                y -= 48f;

                NewCardKey(detailContent, "PROMOTE ANYWAY", 20f, y, 210f, 32f, true,
                    () => DoPromote(member.Id), warn: true);
                NewCardKey(detailContent, "CANCEL", 244f, y, 120f, 32f, false, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                });
                return;
            }

            if (pendingConfirm == Confirm.Demote)
            {
                var crew = roster.CrewOf(member.Id);
                var warn = NewText("Warn", detailContent, 13f, LedgerPalette.Amber,
                    TextAlignmentOptions.TopLeft);
                PlaceTopLeft(warn.rectTransform, 20f, y, CardInnerWidth - 40f, 44f);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.text = LedgerText.DemoteConfirm(member.FirstName,
                    crew != null ? crew.HoodIds.Count : 0);
                y -= 48f;

                NewCardKey(detailContent, "DISBAND", 20f, y, 140f, 32f, true, () =>
                {
                    pendingConfirm = Confirm.None;
                    var result = director.Demote(member.Id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                }, warn: true);
                NewCardKey(detailContent, "CANCEL", 174f, y, 120f, 32f, false, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                });
                return;
            }

            if (assignMode)
            {
                y = DetailLine("Pick a crew, the pool, or the front.",
                    LedgerPalette.Phosphor, y);
                NewCardKey(detailContent, "CANCEL", 20f, y, 120f, 32f, false, () =>
                {
                    assignMode = false;
                    dirty = true;
                });
                return;
            }

            if (member.Rank == Rank.Lieutenant)
            {
                // Ghost in amber: a demotion is the card's dangerous verb, and the
                // filled treatment stays reserved for the confirm that commits it.
                NewCardKey(detailContent, "DEMOTE", 20f, y, 130f, 32f, false, () =>
                {
                    pendingConfirm = Confirm.Demote;
                    dirty = true;
                }, warn: true);
                return;
            }

            // PROMOTE is the card's one loud verb - the filled key; REASSIGN rides
            // beside it as the ghost, so the pair reads primary/secondary at a glance.
            NewCardKey(detailContent, "PROMOTE", 20f, y, 140f, 32f, true, () =>
            {
                var check = director.CheckPromote(member.Id);
                if (!check.CanPromote)
                    lastRefusal = check.Reason;
                else if (check.LowStatWarning)
                    pendingConfirm = Confirm.Promote;
                else
                    DoPromote(member.Id);
                dirty = true;
            });
            NewCardKey(detailContent, "REASSIGN", 174f, y, 150f, 32f, false, () =>
            {
                assignMode = true;
                lastRefusal = "";
                dirty = true;
            });
        }

        void DoPromote(int id)
        {
            pendingConfirm = Confirm.None;
            var result = director.Promote(id, out _);
            lastRefusal = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        // ------------------------------------------------------------------- helpers

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Top-left anchored placement in page coordinates - x right, y DOWN as
        /// a negative anchoredPosition, the convention every block here uses.</summary>
        static void PlaceTopLeft(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>A cell spanning a row's full height at the given x.</summary>
        static void FillRow(RectTransform rect, float x, float w)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(w, 0f);
        }

        static TextMeshProUGUI NewText(string name, Transform parent, float size,
            Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            // The whole book speaks the pack's body face through this one seam; the
            // masthead and tabs re-dress in the headline face at their call sites.
            LedgerSkinSet.ApplyBody(text);
            return text;
        }

        /// <summary>The ContextMenuUI button recipe as a terminal soft-key: a translucent
        /// phosphor block in a 1px frame with bright text. The background Image is the
        /// raycast target and the Button's tint surface (the tint multiplies, so normal
        /// sits dimmed and hover IS the full glow); the frame never tints.</summary>
        TextMeshProUGUI NewButton(Transform parent, string label, float x, float y,
            float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var rect = NewRect("Button " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            background.color = LedgerPalette.ButtonGlow;
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            // The pack slabs live in the TAB STRIP and nowhere else, at the user's
            // word. Every other key in the ledger is flat print - the translucent
            // phosphor block in its 1px frame, always.
            var colours = button.colors;
            colours.normalColor = LedgerPalette.ButtonNormal;
            colours.highlightedColor = LedgerPalette.ButtonHover;
            colours.pressedColor = LedgerPalette.ButtonPressed;
            button.colors = colours;
            Frame(rect, 1f, LedgerPalette.PhosphorDim);
            button.onClick.AddListener(onClick);

            var text = NewText("Label", rect, 13f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.characterSpacing = 1f;
            text.fontStyle = FontStyles.Bold;
            text.text = label;
            return text;
        }

        /// <summary>The detail CARD's own species of key - the verbs of a man's sheet
        /// (PROMOTE, REASSIGN, the confirms) dressed apart from the blue action slabs
        /// of the shop rows. A primary key is a solid block of phosphor with tube-dark
        /// text - the card's one loud verb; a secondary key is a ghost, a faint face
        /// with phosphor text, rimmed by a 1px frame when sprite-less. Warn swaps the
        /// phosphor for the amber gun on keys that commit something a player might
        /// regret. Never dressed by the pack: these stay flat print whatever the
        /// wardrobe, which is exactly what tells them apart from the shop slabs.</summary>
        TextMeshProUGUI NewCardKey(Transform parent, string label, float x, float y,
            float w, float h, bool primary, UnityEngine.Events.UnityAction onClick,
            bool warn = false)
        {
            var ink = warn ? LedgerPalette.Amber : LedgerPalette.Phosphor;

            var rect = NewRect("Key " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            background.color = primary ? ink : new Color(ink.r, ink.g, ink.b, 0.14f);
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colours = button.colors;
            colours.normalColor = LedgerPalette.ButtonNormal;
            colours.highlightedColor = LedgerPalette.ButtonHover;
            colours.selectedColor = LedgerPalette.ButtonHover;
            colours.pressedColor = LedgerPalette.ButtonPressed;
            button.colors = colours;
            button.onClick.AddListener(onClick);

            // Deliberately UNSKINNED, at the user's word: at soft-key size every pack
            // face reads as an arrowhead. The card's verbs are flat print - a solid
            // block, or a ghost inside its 1px rim.
            if (!primary)
                Frame(rect, 1f, ink);

            var text = NewText("Label", rect, 13f,
                primary ? LedgerPalette.Screen : ink, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.characterSpacing = 1f;
            text.fontStyle = FontStyles.Bold;
            text.text = label;
            return text;
        }

        /// <summary>A TOOLBAR segment: the same click surface as NewButton for the
        /// furniture rows (filter bar, CLOSE, the sort menu). Flat print like every
        /// key outside the tab strip - the pack chips read as arrowheads at segment
        /// size, so the two species collapse into the one the terminal always drew.</summary>
        TextMeshProUGUI NewToolbarButton(Transform parent, string label, float x,
            float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var rect = NewRect("Toolbar " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            background.color = LedgerPalette.ButtonGlow;
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colours = button.colors;
            colours.normalColor = LedgerPalette.ButtonNormal;
            colours.highlightedColor = LedgerPalette.ButtonHover;
            colours.pressedColor = LedgerPalette.ButtonPressed;
            button.colors = colours;
            Frame(rect, 1f, LedgerPalette.PhosphorDim);
            button.onClick.AddListener(onClick);

            var text = NewText("Label", rect, 13f, LedgerPalette.Phosphor,
                TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.characterSpacing = 1f;
            text.fontStyle = FontStyles.Bold;
            text.text = label;
            return text;
        }

        /// <summary>A flat single-colour border: four strips. The terminal's line-drawn
        /// box, for anything ON the screen (buttons, cards, the photo slot) - the plastic
        /// Bevel below is reserved for the hardware around it.</summary>
        static void Frame(RectTransform rect, float thickness, Color color)
        {
            BevelEdge(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), thickness, color);
            BevelEdge(rect, new Vector2(0f, 0f), new Vector2(0f, 1f), thickness, color);
            BevelEdge(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), thickness, color);
            BevelEdge(rect, new Vector2(1f, 0f), new Vector2(1f, 1f), thickness, color);
        }

        static void BevelEdge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            float thickness, Color color)
        {
            var edge = NewRect("Bevel", parent);
            edge.anchorMin = anchorMin;
            edge.anchorMax = anchorMax;
            var horizontal = anchorMin.y == anchorMax.y;
            edge.pivot = new Vector2(anchorMin.x, anchorMin.y);
            edge.anchoredPosition = Vector2.zero;
            edge.sizeDelta = horizontal
                ? new Vector2(0f, thickness)
                : new Vector2(thickness, 0f);
            var image = edge.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>An invisible Button over a row whose visible Image doubles as its
        /// target graphic - the whole row is the click surface.</summary>
        static void AddRowButton(RectTransform rect, Image background,
            UnityEngine.Events.UnityAction onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
        }
    }
}
