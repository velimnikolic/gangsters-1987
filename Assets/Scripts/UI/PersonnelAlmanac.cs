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
    /// the WantedHud trick - sprite-less Images stood on their corner - because the TMP
    /// default font has no star glyph to trust; a half star is the same diamond behind a
    /// half-width RectMask2D.
    ///
    /// Repaint is the versioned rebuild the HUDs use: the list and detail card are torn
    /// down and rebuilt when PersonnelDirector.Version or any local view state moves.
    /// Mutations are click-paced, so a few hundred objects per rebuild is the affordable
    /// choice ContextMenuUI already made; pooling waits for a profiler to ask for it.
    /// </summary>
    public sealed class PersonnelAlmanac : MonoBehaviour
    {
        const int SortingOrder = 110;

        const float PageWidth = 1840f;
        const float PageHeight = 1020f;

        const float ListLeft = 40f;
        const float ListWidth = 1120f;

        /// <summary>Pages start below the masthead, the tab strip and their rules.</summary>
        const float PageTop = -128f;

        /// <summary>The personnel list sits under its own filter bar inside the page.</summary>
        const float ListTop = -176f;
        const float ListHeight = 812f;

        const float DetailLeft = 1180f;
        const float DetailWidth = PageWidth - DetailLeft - 36f;

        const float CrewHeaderHeight = 44f;
        const float SectionHeaderHeight = 38f;
        const float RowHeight = 34f;
        const float HoodIndent = 28f;

        /// <summary>Reference pixels of list travel per scroll unit. Tuned for one wheel
        /// notch to move about a row; trackpads arrive as many small deltas and feel the
        /// same.</summary>
        const float WheelStep = 30f;

        const float StarSize = 15f;
        const float StarPitch = 24f;

        /// <summary>True while the book is open. Every world-input reader checks this -
        /// the keyboard half of the modal shield (the raycast-target page is the pointer
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
        Image cursor;
        TMP_Text titleText;

        LedgerPage currentPage = LedgerPage.Newspaper;
        readonly GameObject[] pageRoots = new GameObject[6];
        readonly Image[] tabFaces = new Image[6];
        readonly TMP_Text[] tabLabels = new TMP_Text[6];
        RectTransform listViewport;
        RectTransform listContent;
        RectTransform detailContent;
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
                if (pendingConfirm != Confirm.None)
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

            // The prompt's heartbeat: 0.6s on, 0.46s off - the asymmetry is what real
            // terminal cursors did, and a 50/50 blink reads oddly mechanical next to one.
            if (cursor)
                cursor.enabled = Time.unscaledTime % 1.06f < 0.6f;

            var outfitVersion = outfit ? outfit.Version : 0;
            if (dirty || paintedVersion != director.Version ||
                paintedOutfitVersion != outfitVersion)
            {
                paintedVersion = director.Version;
                paintedOutfitVersion = outfitVersion;
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
                }
                UpdateBarLabels();
            }
        }

        int paintedOutfitVersion = -1;

        void Open()
        {
            if (!page || director.Roster == null)
                return;

            page.SetActive(true);
            IsOpen = true;
            // The book always opens on the newspaper - the week's narrative frame -
            // and the working pages keep their state for when the player turns to them.
            SetPage(LedgerPage.Newspaper);
        }

        void Close()
        {
            if (page)
                page.SetActive(false);
            IsOpen = false;
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
            if (currentPage == LedgerPage.Personnel)
            {
                scrollY = Mathf.Clamp(scrollY - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, scrollY);
            }
            else
            {
                armoryScrollY = Mathf.Clamp(armoryScrollY - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, armoryScrollY);
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

            // The modal shield: the ONE non-button raycast target in the project. With it
            // under the pointer, IsPointerOverGameObject is true everywhere on screen.
            var shade = page.AddComponent<Image>();
            shade.sprite = null;
            shade.color = LedgerPalette.Room;
            shade.raycastTarget = true;

            // The monitor's beige plastic case, raised off the desk; the tube sits
            // sunken inside it - two bevels, opposite ways, and it reads as hardware.
            var casing = NewRect("Case", page.transform);
            casing.anchorMin = casing.anchorMax = new Vector2(0.5f, 0.5f);
            casing.pivot = new Vector2(0.5f, 0.5f);
            casing.sizeDelta = new Vector2(PageWidth + 76f, PageHeight + 76f);
            var casingImage = casing.gameObject.AddComponent<Image>();
            casingImage.sprite = null;
            casingImage.color = LedgerPalette.Case;
            casingImage.raycastTarget = false;
            Bevel(casing, 3f, raised: true);

            var paper = NewRect("Paper", page.transform);
            paper.anchorMin = paper.anchorMax = new Vector2(0.5f, 0.5f);
            paper.pivot = new Vector2(0.5f, 0.5f);
            paper.sizeDelta = new Vector2(PageWidth, PageHeight);
            var paperImage = paper.gameObject.AddComponent<Image>();
            paperImage.sprite = null;
            paperImage.color = LedgerPalette.Screen;
            paperImage.raycastTarget = false;
            Bevel(paper, 3f, raised: false);

            BuildTitleBar(paper);
            BuildRule(paper, -66f);
            BuildTabs(paper);
            BuildRule(paper, -118f);

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
            Frame(detailBack, 1f, LedgerPalette.PhosphorDim);

            detailContent = NewRect("DetailContent", detailBack);
            detailContent.anchorMin = Vector2.zero;
            detailContent.anchorMax = Vector2.one;
            detailContent.offsetMin = detailContent.offsetMax = Vector2.zero;

            BuildSortMenu(personnel);

            // ---- the other sheets ----
            BuildNewspaperPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildComingPage(paper, LedgerPage.Diplomacy, "DIPLOMACY");
            BuildComingPage(paper, LedgerPage.Orders, "ORDERS");

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
            const float width = 176f;
            const float gap = 8f;

            for (var i = 0; i < names.Length; i++)
            {
                var kind = (LedgerPage)i;
                var rect = NewRect("Tab " + names[i], paper);
                PlaceTopLeft(rect, ListLeft + i * (width + gap), -74f, width, 36f);

                var face = rect.gameObject.AddComponent<Image>();
                face.sprite = null;
                face.color = LedgerPalette.ButtonGlow;
                face.raycastTarget = true;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                var colours = button.colors;
                colours.normalColor = LedgerPalette.ButtonNormal;
                colours.highlightedColor = LedgerPalette.ButtonHover;
                colours.pressedColor = LedgerPalette.ButtonPressed;
                button.colors = colours;
                button.onClick.AddListener(() => SetPage(kind));

                Frame(rect, 1f, LedgerPalette.PhosphorDim);

                var label = NewText("Label", rect, 13f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.Center);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                label.characterSpacing = 2f;
                label.fontStyle = FontStyles.Bold;
                label.text = names[i];

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
                tabFaces[i].color = active ? LedgerPalette.Phosphor : LedgerPalette.ButtonGlow;
                tabLabels[i].color = active ? LedgerPalette.Screen : LedgerPalette.Phosphor;
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
            const float leadWidth = 1120f;
            var lead = NewRect("Lead", root);
            PlaceTopLeft(lead, ListLeft, PageTop - 116f, leadWidth, 320f);

            var headline = NewText("Headline", lead, 32f, LedgerPalette.Phosphor,
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
            var photo = NewRect("Photo", root);
            PlaceTopLeft(photo, DetailLeft, PageTop - 116f, DetailWidth, 340f);
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
            PlaceTopLeft(caption.rectTransform, DetailLeft, PageTop - 462f,
                DetailWidth, 40f);
            caption.fontStyle = FontStyles.Italic;
            caption.textWrappingMode = TextWrappingModes.Normal;
            caption.text = "The waterfront at dusk, where the new money drinks.";

            // ---- body columns under the lead ----
            const float columnGap = 28f;
            var columnWidth = (leadWidth - 2f * columnGap) / 3f;
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
                    PageTop - 452f, columnWidth, 380f);
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

            NewButton(root, "< EARLIER", DetailLeft + 180f, PageTop, 130f, 32f, () =>
            {
                var sheets = outfit ? outfit.Accounts.Sheets.Count : 1;
                if (financeWeekBack < sheets - 1)
                {
                    financeWeekBack++;
                    dirty = true;
                }
            });
            NewButton(root, "LATER >", DetailLeft + 326f, PageTop, 130f, 32f, () =>
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

            var heading = NewText("Heading", financesContent, 20f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(heading.rectTransform, ListLeft, PageTop, 700f, 32f);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 3f;
            heading.text = "WEEKLY BALANCE SHEET — WEEK " + report.Week +
                           (report.Closed ? "  [CLOSED]" : "");

            // ---- income left, outgoings right ----
            var y = PageTop - 48f;

            FinanceRow(ListLeft, y, ":: INCOME", "", LedgerPalette.PhosphorDim, false);
            FinanceRow(ListLeft + 580f, y, ":: OUTGOINGS", "", LedgerPalette.PhosphorDim,
                false);
            y -= 30f;

            FinanceRow(ListLeft, y, "Legal", LedgerText.Cash(report.LegalIncome),
                LedgerPalette.Phosphor, false);
            FinanceRow(ListLeft + 580f, y, "Wages",
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

            FinanceRow(ListLeft + 580f, y, "   " + hoods + " hoods",
                LedgerText.Cash(hoodWages), LedgerPalette.PhosphorDim, false);
            y -= 26f;

            FinanceRow(ListLeft, y, "Sales", LedgerText.Cash(report.SalesIncome),
                LedgerPalette.Phosphor, false);
            FinanceRow(ListLeft + 580f, y, "   " + lieutenants + " lieutenants",
                LedgerText.Cash(lieutenantWages), LedgerPalette.PhosphorDim, false);
            y -= 26f;

            if (specialists > 0)
            {
                FinanceRow(ListLeft + 580f, y, "   " + specialists + " on retainer",
                    LedgerText.Cash(specialistWages), LedgerPalette.PhosphorDim, false);
                y -= 26f;
            }

            FinanceRow(ListLeft + 580f, y, "Bribes", LedgerText.Cash(report.Bribes),
                LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft + 580f, y, "Purchases", LedgerText.Cash(report.Purchases),
                LedgerPalette.Phosphor, false);
            y -= 26f;
            FinanceRow(ListLeft + 580f, y, "Other costs", LedgerText.Cash(report.OtherCosts),
                LedgerPalette.Phosphor, false);
            y -= 34f;

            FinanceRow(ListLeft, y, "TOTAL IN", LedgerText.Cash(report.TotalIncome),
                LedgerPalette.Phosphor, true);
            FinanceRow(ListLeft + 580f, y, "TOTAL OUT", LedgerText.Cash(report.TotalOutgoings),
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
                PlaceTopLeft(stamp.rectTransform, ListLeft, y, 900f, 40f);
                stamp.text = "A closed week - the record of what moved. Current holdings " +
                             "live on the open sheet.";
            }

            // ---- the plain-language note, right column ----
            var note = NewParagraph("Note", financesContent, 13f, LedgerPalette.PhosphorDim);
            PlaceTopLeft(note.rectTransform, DetailLeft, PageTop - 64f, DetailWidth, 400f);
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
            PlaceTopLeft(labelText.rectTransform, x, y, 380f, 24f);
            if (bold)
                labelText.fontStyle = FontStyles.Bold;
            labelText.text = label;

            if (value.Length == 0)
                return;

            var valueText = NewText("Value", financesContent, bold ? 15f : 14f, color,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(valueText.rectTransform, x + 340f, y, 180f, 24f);
            if (bold)
                valueText.fontStyle = FontStyles.Bold;
            valueText.text = value;
        }

        // --------------------------------------------------------------- the armory page

        RectTransform armoryContent;
        RectTransform armoryInventoryViewport;
        RectTransform armoryInventoryContent;
        float armoryScrollY;

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
            PlaceTopLeft(armoryInventoryViewport, 700f, PageTop - 76f,
                PageWidth - 700f - 36f, ListHeight - 76f);
            armoryInventoryViewport.gameObject.AddComponent<RectMask2D>();

            armoryInventoryContent = NewRect("Rows", armoryInventoryViewport);
            armoryInventoryContent.anchorMin = new Vector2(0f, 1f);
            armoryInventoryContent.anchorMax = new Vector2(1f, 1f);
            armoryInventoryContent.pivot = new Vector2(0f, 1f);
            armoryInventoryContent.anchoredPosition = Vector2.zero;
            armoryInventoryContent.sizeDelta = new Vector2(0f, ListHeight - 76f);
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
                PlaceTopLeft(note.rectTransform, ListLeft, PageTop - 34f, 1200f, 22f);
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
            var buyLabel = NewButton(armoryContent, "[ BUY ]", ListLeft + 440f, y + 1f,
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

            return y - 52f;
        }

        void BuildInventory(Roster roster)
        {
            var header = NewText("InvHeader", armoryContent, 14f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(header.rectTransform, 700f, PageTop - 46f, 500f, 24f);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 2f;
            header.text = ":: STOCK — " + roster.Equipment.Count + " ITEMS";

            var y = 0f;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                var row = NewRect("Item", armoryInventoryContent);
                PlaceTopLeft(row, 0f, y, PageWidth - 700f - 36f, 28f);

                var kind = NewText("Kind", row, 12f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(kind.rectTransform, 0f, 110f);
                kind.text = LedgerText.EquipmentLabel(item.Kind).ToUpperInvariant();

                var name = NewText("Name", row, 14f, LedgerPalette.Phosphor,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(name.rectTransform, 116f, 240f);
                name.text = item.DisplayName;

                var holder = roster.Find(item.HolderId);
                var holderText = NewText("Holder", row, 13f,
                    holder != null ? LedgerPalette.Phosphor : LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(holderText.rectTransform, 370f, 330f);
                holderText.text = holder != null ? holder.FullName : "in armory";

                var itemId = item.Id;
                if (holder != null)
                    NewButton(row, "[ RETURN ]", PageWidth - 700f - 36f - 120f, -2f,
                        116f, 24f, () =>
                        {
                            var result = director.ReturnEquipment(itemId);
                            armoryNote = result.Ok ? "" : result.Reason;
                            dirty = true;
                        });
                else
                    NewButton(row, "[ GIVE ]", PageWidth - 700f - 36f - 120f, -2f,
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

        /// <summary>GIVE's second step: the stock list becomes the roster, pick the
        /// holder. The tommy gun tags the poor shots in amber before the mistake, and
        /// permits it after - the promotion rule's discipline.</summary>
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
            PlaceTopLeft(header.rectTransform, 700f, PageTop - 46f, 700f, 24f);
            header.fontStyle = FontStyles.Bold;
            header.text = "GIVE " + item.DisplayName.ToUpperInvariant() + " TO:";

            NewButton(armoryContent, "[ CANCEL ]", PageWidth - 396f, PageTop - 46f,
                120f, 24f, () =>
                {
                    givePickerItemId = -1;
                    dirty = true;
                });

            var y = 0f;
            var tommy = item.Kind == EquipmentKind.TommyGun;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Dead)
                    continue;

                var row = NewRect("Pick", armoryInventoryContent);
                PlaceTopLeft(row, 0f, y, PageWidth - 700f - 36f, 28f);

                var poorShot = tommy && member.GetHalfSteps(CharacterAttribute.Firearms) <
                    Outfit.ArmoryCatalog.TommyGunFirearmsFloor;

                var memberId = member.Id;
                var memberName = member.FullName;
                var pick = NewButton(row, "", 0f, 0f, 300f, 26f, () =>
                {
                    var result = director.GiveEquipment(givePickerItemId, memberId);
                    armoryNote = !result.Ok ? result.Reason
                        : poorShot ? LedgerText.TommyGunWarning(memberName)
                        : "";
                    givePickerItemId = -1;
                    dirty = true;
                });
                pick.alignment = TextAlignmentOptions.MidlineLeft;
                pick.margin = new Vector4(10f, 0f, 0f, 0f);
                pick.text = member.FullName.ToUpperInvariant();

                var guns = NewText("Guns", row, 13f, LedgerPalette.PhosphorDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(guns.rectTransform, 320f, 200f);
                guns.text = "Firearms " + LedgerText.Stars(
                    member.GetHalfSteps(CharacterAttribute.Firearms));

                if (poorShot)
                {
                    var warn = NewText("Warn", row, 12f, LedgerPalette.Amber,
                        TextAlignmentOptions.MidlineLeft);
                    FillRow(warn.rectTransform, 540f, 200f);
                    warn.fontStyle = FontStyles.Bold;
                    warn.text = "POOR SHOT";
                }

                y -= 30f;
            }

            SizeInventoryContent(-y);
        }

        void SizeInventoryContent(float height)
        {
            var viewportHeight = ListHeight - 76f;
            armoryInventoryContent.sizeDelta =
                new Vector2(0f, Mathf.Max(viewportHeight, height + 8f));
            var maxScroll = Mathf.Max(0f, armoryInventoryContent.sizeDelta.y - viewportHeight);
            armoryScrollY = Mathf.Clamp(armoryScrollY, 0f, maxScroll);
            armoryInventoryContent.anchoredPosition = new Vector2(0f, armoryScrollY);
        }

        void BuildScanlines(RectTransform paper)
        {
            var root = NewRect("Scanlines", paper);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            for (var y = 3f; y < PageHeight; y += 6f)
            {
                var line = NewRect("Scan", root);
                PlaceTopLeft(line, 0f, -y, PageWidth, 1f);
                var image = line.gameObject.AddComponent<Image>();
                image.sprite = null;
                image.color = LedgerPalette.ScanLine;
                image.raycastTarget = false;
            }
        }

        void BuildTitleBar(RectTransform paper)
        {
            titleText = NewText("Title", paper, 26f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(titleText.rectTransform, ListLeft, -14f, 900f, 46f);
            titleText.fontStyle = FontStyles.Bold;
            titleText.characterSpacing = 6f;
            // The date is written by UpdateBarLabels from the campaign calendar - the
            // year was a hard-coded literal here once, and only once.

            // The block cursor at the prompt's end - Update blinks it. Nothing says
            // "1980 terminal" for fewer objects than one square going on and off.
            var cursorRect = NewRect("Cursor", paper);
            PlaceTopLeft(cursorRect, ListLeft + 480f, -26f, 14f, 24f);
            cursor = cursorRect.gameObject.AddComponent<Image>();
            cursor.sprite = null;
            cursor.color = LedgerPalette.Phosphor;
            cursor.raycastTarget = false;

            titleCount = NewText("Count", paper, 15f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(titleCount.rectTransform, PageWidth - 560f, -20f, 380f, 36f);
            titleCount.characterSpacing = 2f;

            NewButton(paper, "[ CLOSE ]", PageWidth - 156f, -18f, 120f, 34f, Close);
        }

        void BuildFilterBar(RectTransform parent)
        {
            sortLabel = NewButton(parent, "", ListLeft, PageTop, 360f, 36f, ToggleSortMenu);
            rankLabel = NewButton(parent, "", ListLeft + 380f, PageTop, 230f, 36f, CycleRank);
            postLabel = NewButton(parent, "", ListLeft + 630f, PageTop, 210f, 36f, CyclePost);
            showLabel = NewButton(parent, "", ListLeft + 860f, PageTop, 210f, 36f, CycleShow);
            UpdateBarLabels();
        }

        void BuildRule(RectTransform paper, float y)
        {
            var rule = NewRect("Rule", paper);
            PlaceTopLeft(rule, ListLeft, y, PageWidth - ListLeft - 36f, 1f);
            var image = rule.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = LedgerPalette.PhosphorDim;
            image.raycastTarget = false;
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

                var button = NewButton(rect, label, 4f, -4f - i * rowH, 352f, rowH, () =>
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
            var inCrew = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        inCrew = true;
                        BuildCrewHeader(roster, row, y);
                        y -= CrewHeaderHeight;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        inCrew = false;
                        BuildSectionHeader(row.Kind, y);
                        y -= SectionHeaderHeight;
                        break;

                    case RowKind.Lieutenant:
                        BuildCharacterRow(roster, row.CharacterId, y, indent: false,
                            lieutenantRow: true);
                        y -= RowHeight;
                        break;

                    default:
                        BuildCharacterRow(roster, row.CharacterId, y, inCrew);
                        y -= RowHeight;
                        break;
                }
            }

            listContent.sizeDelta = new Vector2(0f, Mathf.Max(ListHeight, -y + 8f));
            var maxScroll = Mathf.Max(0f, listContent.sizeDelta.y - ListHeight);
            scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);
            listContent.anchoredPosition = new Vector2(0f, scrollY);
        }

        void BuildCrewHeader(Roster roster, LedgerRow row, float y)
        {
            var lieutenant = roster.Find(row.CharacterId);
            var crewId = row.CrewId;

            var rect = NewRect("Crew", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, CrewHeaderHeight);

            var isTarget = assignMode && selectedId >= 0;
            // Inverse video is the terminal's selection: the block lights, the text goes
            // tube-dark. Same convention on the hood rows below.
            var inverse = !assignMode && row.CharacterId == selectedId;
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            background.color = isTarget ? LedgerPalette.Target
                : inverse ? LedgerPalette.Phosphor
                : LedgerPalette.BandDim;
            background.raycastTarget = true;

            AddRowButton(rect, background, () =>
            {
                if (assignMode)
                    FinishAssign(director.AssignToCrew(selectedId, crewId));
                else
                    SelectMember(row.CharacterId);
            });

            var name = NewText("Name", rect, 16f,
                inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            FillRow(name.rectTransform, 12f, 340f);
            name.fontStyle = FontStyles.Bold;
            name.characterSpacing = 2f;
            name.text = lieutenant != null
                ? LedgerText.CrewName(lieutenant.Surname)
                : "CREW";
            // No member cells here: the lieutenant is a member, and his row - the
            // Lieutenant row right below - carries his data like anyone else's.
        }

        void BuildSectionHeader(RowKind kind, float y)
        {
            var rect = NewRect("Section", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, SectionHeaderHeight);

            var isTarget = assignMode && selectedId >= 0 && kind != RowKind.SpecialistHeader;
            if (isTarget)
            {
                var background = rect.gameObject.AddComponent<Image>();
                background.sprite = null;
                background.color = LedgerPalette.Target;
                background.raycastTarget = true;

                var toPool = kind == RowKind.PoolHeader;
                AddRowButton(rect, background, () => FinishAssign(toPool
                    ? director.AssignToPool(selectedId)
                    : director.AssignToFront(selectedId)));
            }

            var label = NewText("Label", rect, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            FillRow(label.rectTransform, 12f, 400f);
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 3f;
            label.text = kind switch
            {
                RowKind.FrontHeader => "== THE FRONT ==",
                RowKind.PoolHeader => "== THE POOL ==",
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
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = null;
            // No zebra stripes - the raster's own scanlines rule this page. The
            // background catches the click, and lights to full phosphor for the
            // selection: inverse video, the terminal's one and only highlight.
            background.color = inverse ? LedgerPalette.Phosphor : Color.clear;
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

            // In assign mode a man is not a target - clicking one cancels the mode, the
            // gesture for "never mind" that costs nothing to discover.
            AddRowButton(rect, background, () =>
            {
                if (assignMode)
                {
                    assignMode = false;
                    dirty = true;
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
            FillRow(name.rectTransform, x, 320f);
            if (lieutenantRow)
                name.fontStyle = FontStyles.Bold;
            name.text = member.FullName.ToUpperInvariant();

            BuildRowCells(rect, member, 360f, dim: dim, inverse: inverse);
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

            var rank = NewText("Rank", rect, 12f, muted, TextAlignmentOptions.MidlineLeft);
            FillRow(rank.rectTransform, x, 110f);
            rank.text = (member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank)).ToUpperInvariant();

            if (member.Status != CharacterStatus.Active)
            {
                var status = NewText("Status", rect, 12f,
                    inverse ? LedgerPalette.Screen
                    : dim ? LedgerPalette.Disabled
                    : LedgerPalette.Amber,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(status.rectTransform, x + 120f, 120f);
                status.fontStyle = FontStyles.Bold;
                status.text = LedgerText.StatusLabel(member.Status).ToUpperInvariant();
            }

            if (member.Wanted)
            {
                var diamond = NewRect("Wanted", rect);
                diamond.anchorMin = new Vector2(0f, 0.5f);
                diamond.anchorMax = new Vector2(0f, 0.5f);
                diamond.pivot = new Vector2(0.5f, 0.5f);
                diamond.anchoredPosition = new Vector2(x + 256f, 0f);
                diamond.sizeDelta = new Vector2(10f, 10f);
                diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var image = diamond.gameObject.AddComponent<Image>();
                image.sprite = null;
                image.color = LedgerPalette.Amber;
                image.raycastTarget = false;
            }

            var count = director.Roster.HeldCount(member.Id);
            if (count > 0)
            {
                var items = NewText("Items", rect, 12f, muted,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(items.rectTransform, x + 280f, 90f);
                items.text = count == 1 ? "1 item" : count + " items";
            }

            if (options.Sort != SortKey.Roster && !assignMode)
            {
                var value = NewText("Value", rect, 15f,
                    inverse ? LedgerPalette.Screen : LedgerPalette.Phosphor,
                    TextAlignmentOptions.MidlineRight);
                FillRow(value.rectTransform, ListWidth - 92f, 80f);
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

        void RebuildDetail()
        {
            foreach (Transform old in detailContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
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

            // The mugshot corner: no portrait art exists, so the placeholder is the
            // organizer's photo slot - a sunken dark square with the man's initials.
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

            var name = NewText("Name", detailContent, 22f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(name.rectTransform, 118f, -20f, DetailWidth - 138f, 32f);
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
            PlaceTopLeft(post.rectTransform, 118f, -54f, DetailWidth - 138f, 22f);
            post.text = rankLine + "  ·  " +
                LedgerText.AssignmentLine(assignment, crewName);

            var statusColor = member.Status == CharacterStatus.Active
                ? LedgerPalette.PhosphorDim
                : LedgerPalette.Amber;
            var status = NewText("Status", detailContent, 14f, statusColor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(status.rectTransform, 118f, -78f, DetailWidth - 138f, 22f);
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

        float DetailLine(string text, Color color, float y)
        {
            var line = NewText("Line", detailContent, 14f, color,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(line.rectTransform, 20f, y, DetailWidth - 40f, 22f);
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
            PlaceTopLeft(back, 140f, y - 4f, 260f, 12f);
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = null;
            backImage.color = LedgerPalette.PhosphorFaint;
            backImage.raycastTarget = false;

            var fill = NewRect("Fill", back);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(260f * (member.Loyalty / 100f), 0f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = null;
            fillImage.color = LedgerPalette.Phosphor;
            fillImage.raycastTarget = false;

            var value = NewText("Value", detailContent, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(value.rectTransform, DetailWidth - 90f, y, 70f, 20f);
            value.text = member.Loyalty.ToString();

            return y - 26f;
        }

        float BuildAttributeRow(Character member, CharacterAttribute attribute, float y)
        {
            var label = NewText("Label", detailContent, 14f, LedgerPalette.Phosphor,
                TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(label.rectTransform, 20f, y, 160f, 22f);
            label.text = LedgerText.AttributeLabel(attribute);

            var halfSteps = member.GetHalfSteps(attribute);
            BuildStarStrip(190f, y - 11f, halfSteps);

            var value = NewText("Value", detailContent, 13f, LedgerPalette.PhosphorDim,
                TextAlignmentOptions.MidlineRight);
            PlaceTopLeft(value.rectTransform, DetailWidth - 90f, y, 70f, 22f);
            value.text = LedgerText.Stars(halfSteps);

            return y - 26f;
        }

        /// <summary>
        /// Five diamond slots. A full star is the WantedHud diamond; a HALF star is the
        /// same diamond centred on the slot but parented under a RectMask2D covering only
        /// the slot's left half, which clips it to a left-pointing wedge. The mask spans
        /// the diamond's rotated bounding box (side * sqrt2), not the sprite size - masking
        /// to the unrotated size would shave the wedge's point off.
        /// </summary>
        void BuildStarStrip(float x, float centreY, int halfSteps)
        {
            var span = StarSize * 1.41421f;

            for (var slot = 0; slot < 5; slot++)
            {
                var cx = x + slot * StarPitch + StarPitch * 0.5f;

                BuildDiamond(detailContent, cx, centreY, LedgerPalette.PhosphorFaint);

                if (halfSteps >= (slot + 1) * 2)
                {
                    BuildDiamond(detailContent, cx, centreY, LedgerPalette.Phosphor);
                }
                else if (halfSteps == slot * 2 + 1)
                {
                    var mask = NewRect("Half", detailContent);
                    mask.anchorMin = new Vector2(0f, 1f);
                    mask.anchorMax = new Vector2(0f, 1f);
                    mask.pivot = new Vector2(0.5f, 0.5f);
                    mask.anchoredPosition = new Vector2(cx - span * 0.25f, centreY);
                    mask.sizeDelta = new Vector2(span * 0.5f, span);
                    mask.gameObject.AddComponent<RectMask2D>();

                    var star = NewRect("Star", mask);
                    star.anchorMin = star.anchorMax = new Vector2(0.5f, 0.5f);
                    star.pivot = new Vector2(0.5f, 0.5f);
                    star.anchoredPosition = new Vector2(span * 0.25f, 0f);
                    star.sizeDelta = new Vector2(StarSize, StarSize);
                    star.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    var image = star.gameObject.AddComponent<Image>();
                    image.sprite = null;
                    image.color = LedgerPalette.Phosphor;
                    image.raycastTarget = false;
                }
            }
        }

        void BuildDiamond(Transform parent, float cx, float cy, Color color)
        {
            var rect = NewRect("Diamond", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(cx, cy);
            rect.sizeDelta = new Vector2(StarSize, StarSize);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = color;
            image.raycastTarget = false;
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
                NewButton(detailContent, "[ RETURN ]", DetailWidth - 130f, y + 24f, 110f, 22f,
                    () =>
                    {
                        lastRefusal = "";
                        var result = director.ReturnEquipment(item.Id);
                        if (!result.Ok)
                            lastRefusal = result.Reason;
                        dirty = true;
                    });
            }

            y = DetailLine(":: ARMORY", LedgerPalette.PhosphorDim, y - 4f);

            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.HolderId == member.Id)
                    continue;
                anyStock = true;

                if (item.HolderId == RosterEquipment.Unheld)
                {
                    y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                        item.DisplayName, LedgerPalette.Phosphor, y + 2f);
                    if (member.Status != CharacterStatus.Dead)
                        NewButton(detailContent, "[ GIVE ]", DetailWidth - 130f, y + 24f,
                            110f, 22f, () =>
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
                    // The finite pool made visible: an item someone else holds shows here,
                    // muted, with no button - the only path is returning it from HIS card,
                    // so exclusivity cannot be violated from the UI at all.
                    var holder = roster.Find(item.HolderId);
                    y = DetailLine(LedgerText.EquipmentLabel(item.Kind) + "  ·  " +
                        item.DisplayName + "  —  " +
                        LedgerText.HeldByLine(holder != null ? holder.FullName : "?"),
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
                PlaceTopLeft(warn.rectTransform, 20f, y, DetailWidth - 40f, 44f);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.text = LedgerText.PromoteWarning(member.FullName);
                y -= 48f;

                NewButton(detailContent, "[ PROMOTE ANYWAY ]", 20f, y, 210f, 30f,
                    () => DoPromote(member.Id));
                NewButton(detailContent, "[ CANCEL ]", 244f, y, 120f, 30f, () =>
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
                PlaceTopLeft(warn.rectTransform, 20f, y, DetailWidth - 40f, 44f);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.text = LedgerText.DemoteConfirm(member.FirstName,
                    crew != null ? crew.HoodIds.Count : 0);
                y -= 48f;

                NewButton(detailContent, "[ DISBAND ]", 20f, y, 140f, 30f, () =>
                {
                    pendingConfirm = Confirm.None;
                    var result = director.Demote(member.Id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                });
                NewButton(detailContent, "[ CANCEL ]", 174f, y, 120f, 30f, () =>
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
                NewButton(detailContent, "[ CANCEL ]", 20f, y, 120f, 30f, () =>
                {
                    assignMode = false;
                    dirty = true;
                });
                return;
            }

            if (member.Rank == Rank.Lieutenant)
            {
                NewButton(detailContent, "[ DEMOTE ]", 20f, y, 130f, 30f, () =>
                {
                    pendingConfirm = Confirm.Demote;
                    dirty = true;
                });
                return;
            }

            NewButton(detailContent, "[ PROMOTE ]", 20f, y, 140f, 30f, () =>
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
            NewButton(detailContent, "[ REASSIGN ]", 174f, y, 150f, 30f, () =>
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
            var colours = button.colors;
            colours.normalColor = LedgerPalette.ButtonNormal;
            colours.highlightedColor = LedgerPalette.ButtonHover;
            colours.pressedColor = LedgerPalette.ButtonPressed;
            button.colors = colours;
            button.onClick.AddListener(onClick);

            Frame(rect, 1f, LedgerPalette.PhosphorDim);

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

        /// <summary>
        /// The 90s bevel: four strips along the edges. Raised = lit top/left, shaded
        /// bottom/right; sunken swaps them. Reserved for the monitor's case and tube -
        /// the hardware; everything on the screen uses Frame.
        /// </summary>
        static void Bevel(RectTransform rect, float thickness, bool raised)
        {
            var light = raised ? LedgerPalette.BevelLight : LedgerPalette.BevelDark;
            var dark = raised ? LedgerPalette.BevelDark : LedgerPalette.BevelLight;

            BevelEdge(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), thickness, light);
            BevelEdge(rect, new Vector2(0f, 0f), new Vector2(0f, 1f), thickness, light);
            BevelEdge(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), thickness, dark);
            BevelEdge(rect, new Vector2(1f, 0f), new Vector2(1f, 1f), thickness, dark);
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
