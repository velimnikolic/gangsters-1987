using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Business;
using LivingCity.Gangs;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The outfit ledger, 1987: a manila file open on the boss's desk, filling the
    /// screen - centred, so an ultrawide monitor puts the desk lamp's light either
    /// side of it instead of stretching the file into a billboard. Divider tabs for
    /// the morning paper, personnel, chain of command, blocks, finances, the armory
    /// catalogue, families and the law sit above the working sheet, with a blotter
    /// strip of readouts under the masthead and the night's telex slips clipped in
    /// beneath it. Opened with P.
    ///
    /// Most pages are bookkeeping. CHAIN OF COMMAND is the administrative exception:
    /// it transfers real Characters through the shared authority. Tactical street
    /// orders are still laid against the city on the map.
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

        /// <summary>The status rail down the left. Fixed: it holds figures, and a figure
        /// column that reflows is a figure column nobody learns.
        ///
        /// The design's 236 held type at 9 and 10 point, which is unreadable on the
        /// screens this is played on. Every face on the rail is set 30% up from that
        /// drawing, and the column widens with them - by MORE than the type, 236 to 330,
        /// so a note that ran to one line at the old measure still runs to one line at
        /// the new one. The rail's own HEIGHT could not follow: the window is 1080 and
        /// the rail was already 97% full at it, so the air between the rows was spent on
        /// the letters instead. Every rect below is cut to the line box the face actually
        /// prints - Plex Mono 1.080 x its point size, Oswald 1.281 - and not a unit more.</summary>
        const float RailW = 330f;

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
            MeasureBlocksLayout();
            MeasureFinancesLayout();
            MeasureArmoryLayout();
            MeasureDiplomacyLayout();
            MeasureCommandLayout();
            MeasureOrdersLayout();
        }

        /// <summary>The book standing in this scene. A HUD key that opens the same
        /// folder the P key opens needs a way to ask for it, and hunting a MonoBehaviour
        /// by type from a click handler is a scan of the whole city. Same registry
        /// convention - and the same Play reset - as every other layer here.</summary>
        public static PersonnelAlmanac Instance { get; private set; }

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
            Instance = null;
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
            Command,
            Blocks,
            Finances,
            Armory,
            Diplomacy,
            Law,
            Orders,

            /// <summary>The blueprint of ONE building's flats. Tab-less like Orders: it is
            /// about a building, not about the outfit, so it is opened from the block file
            /// - the building's mast on the film, or its header in the trade column - and
            /// never from the strip (EPIC 27).</summary>
            Blueprint,
            Wire, // Appended to preserve existing page IDs.
        }

        /// <summary>The tabs the folder actually shows, in strip order. ORDERS and
        /// BLUEPRINT deliberately have no tabs; their roots still build so the relevant
        /// workflow can reach them in code.</summary>
        static readonly string[] TabNames =
        {
            "THE PAPER", "THE WIRE", "CHAIN OF COMMAND", "BLOCKS", "FINANCES", "ARMORY",
            "FAMILIES", "THE LAW",
        };

        /// <summary>Tab navigation is explicit because the page enum also contains
        /// tab-less working pages.</summary>
        static readonly LedgerPage[] TabPages =
        {
            LedgerPage.Newspaper, LedgerPage.Wire, LedgerPage.Command, LedgerPage.Blocks,
            LedgerPage.Finances, LedgerPage.Armory, LedgerPage.Diplomacy,
            LedgerPage.Law,
        };

        /// <summary>What a real file's tabs say: the sheet is one leaf of a numbered
        /// file, and the ticker prints which one. Pure furniture, and the design's.</summary>
        static readonly int[] TabFolios = { 1, 4, 8, 10, 12, 14, 16, 17, 18 };
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
        readonly Image[] timeControlFaces = new Image[7];
        readonly TMP_Text[] timeControlLabels = new TMP_Text[7];
        readonly Button[] timeControlButtons = new Button[7];
        TMP_Text chromeClock;

        // Opening the ledger takes a temporary hold on the clock. If the player does
        // not touch a time control, closing restores the state from before the book;
        // an explicit speed/HOLD choice is kept.
        bool ownsLedgerPause;
        bool clockWasPaused;
        bool clockChangedInLedger;

        // ---- the status rail ----
        RectTransform railRoot;
        TMP_Text railClock;
        TMP_Text railPayroll;
        TMP_Text railPayrollNote;

        // ---- the wire down the rail ----
        RectTransform railWire, railWireViewport, railWireRun;
        TMP_Text railWireCount;
        float railWireScroll;

        /// <summary>Last painted WireBook version.</summary>
        int railWirePainted = -1;

        readonly List<WireLine> railWireLines = new List<WireLine>();

        bool railWireThisBlock;
        TerritoryBlockId railWireBlock;

        RectTransform railWireScopeThis, railWireScopeAll;

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
        readonly GameObject[] pageRoots =
            new GameObject[System.Enum.GetValues(typeof(LedgerPage)).Length];
        readonly Image[] tabFaces = new Image[TabNames.Length];
        readonly TMP_Text[] tabLabels = new TMP_Text[TabNames.Length];
        readonly RectTransform[] tabRects = new RectTransform[TabNames.Length];

        PersonnelDirector director;

        /// <summary>The campaign day the book is turned to, which the wage table needs
        /// for a man's service premium (Outfit.Wages.TenureBonus). Read off the roster
        /// rather than the clock, because the roster is what every page here already
        /// holds and the two are written through together at the day tick.</summary>
        int RosterDay => director != null && director.Roster != null
            ? director.Roster.Day
            : 0;
        OutfitDirector outfit;
        readonly WireSheet wireSheet = new WireSheet();
        int paintedWireVersion = -1;
        Ambient.CityClock cityClock;

        /// <summary>Whether the scene has been asked for its clock. A scene with none is
        /// an answer worth keeping, not a search worth repeating sixty times a second.
        /// </summary>
        bool clockSearched;

        /// <summary>Scratch for Turf reads - refilled from the markers on use.</summary>
        readonly List<Outfit.Turf.Holding> holdings = new List<Outfit.Turf.Holding>();

        int paintedVersion = -1;
        int paintedOutfitVersion = -1;
        int paintedGangVersion = -1;
        int paintedTerritoryVersion = -1;
        int paintedTerritoryObservationVersion = -1;
        int paintedRacketVersion = -1;

        /// <summary>The plate the block file was last painted around. A block arrives
        /// from the streamer seconds after the file opens, and the picture is re-exposed
        /// when it does - the paper has to be re-read with it, or the sheet keeps saying
        /// the street is still coming up over a photograph of a finished block.</summary>
        int paintedExposure = -1;
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
            Instance = this;
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

            // P belongs to the morning paper while that sheet is up; its handler closes
            // the sheet and opens this book on THE PAPER without a second toggle here.
            if (NewspaperHud.ClaimsPaperKey)
                return;

            if (!IsOpen && keyboard.pKey.wasPressedThisFrame &&
                (BlocksTargetingActive || OrdersTargetingActive))
            {
                MapTargeting.Surface?.Dismiss();
                if (BlocksTargetingActive)
                    CancelBlocksTargetingAndReturn();
                else
                    CancelOrderTargetingAndReturn();
                return;
            }

            // A caret owns the alphabet. THE WIRE has the book's second typed field
            // (the blueprint's name is the first), and P closes the book everywhere
            // else - so a reader searching the archive for PAULIE must not lose it at
            // the first letter. Esc gives the keys back before it closes anything.
            if (IsOpen && !blockCardPick.IsValid &&
                currentPage == LedgerPage.Wire && wireSheet.Typing)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    wireSheet.StopTyping();
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
                if (BlocksTargetingActive && mapGone)
                    CancelBlocksTargetingAndReturn();
                else if (OrdersTargetingActive && mapGone)
                    CancelOrderTargetingAndReturn();
                return;
            }

            if (blockCardPick.IsValid)
            {
                // The popup owns keyboard input; the page beneath keeps its selection.
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    CloseTradePopup();
                    return;
                }
            }
            else
            {
                // [ and ] turn the pages; the tabs are the pointer's way. Both walk the
                // TABS, not the page roots - a page with no tab is not in the book.
                var tabIndex = System.Array.IndexOf(TabPages, currentPage);
                if (keyboard.leftBracketKey.wasPressedThisFrame)
                    SetPage(TabPages[tabIndex < 0
                        ? TabPages.Length - 1
                        : (tabIndex + TabPages.Length - 1) % TabPages.Length]);
                if (keyboard.rightBracketKey.wasPressedThisFrame)
                    SetPage(TabPages[(tabIndex < 0 ? 0 : tabIndex + 1) % TabPages.Length]);

                // F2: the sixty-man scale roster - the ledger is specified to stay usable
                // at sixty, and this is how a reviewer sees that without editor wiring.
                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    director.DebugSeedLarge(60);
                    selectedId = -1;
                    listScroll = 0f;
                    dirty = true;
                }

                // F3: THE PHONE RINGS TOMORROW (EPIC 40's bench lever). The conditions the
                // street wants - a lieutenant to bring the word, money for the whole path,
                // our name in the paper - so the man's card comes at the next six o'clock
                // cut and the rest can be walked through by hand.
                if (keyboard.f3Key.wasPressedThisFrame && outfit != null)
                {
                    outfit.DebugRingTomorrow(director);
                    dirty = true;
                }

                // THE WIRE walks on the arrow keys: the register is read line by line
                // without a click, and the rest of its keys scroll the one list it has.
                if (currentPage == LedgerPage.Wire)
                    wireSheet.Keys(keyboard);

                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    // Innermost state first - each Esc peels one layer, closing last.
                    if (currentPage == LedgerPage.Blocks && CloseBlocksTransient())
                    {
                        // The blocks page consumed this Esc.
                    }
                    else if (currentPage == LedgerPage.Command && CloseCommandTransient())
                    {
                        // The chain of command consumed this Esc.
                    }
                    else if (currentPage == LedgerPage.Blueprint && CloseBlueprintTransient())
                    {
                        // The blueprint consumed this Esc: the flat's form first, then the
                        // sheet itself, which gives back the page it was opened over.
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
            // The frame is the book's own; the canvas it stands on can still be smaller
            // than it, so the fit is read every turn rather than only at build.
            FitPage();

            UpdateScroll();
            TickFamilies();
            RefreshClock();
            RefreshTimeControls();
            RunTelex();

            var outfitVersion = outfit ? outfit.Version : 0;
            var exposure = blockCardId.IsValid ? BlockFilm.Get().Exposures : -1;
            var territoryVersion = TerritoryRuntime.Instance
                ? TerritoryRuntime.Instance.StateVersion
                : -1;
            var territoryObservationVersion = TerritoryRuntime.Instance
                ? TerritoryRuntime.Instance.ObservationVersion
                : -1;
            // What one shopkeeper said is not a block figure: a shop going from wavering
            // to shaken leaves the block's compliance share exactly where it was, so the
            // state version never moves and an open block file would keep printing the
            // line it was painted with over a shop that has since been smashed.
            var racketVersion = TerritoryRuntime.Instance
                ? TerritoryRuntime.Instance.RacketVersion
                : 0;
            // The reader is holding the block on the organization sheet. A repaint
            // destroys the sheet whole and with it the model under their hand, and this
            // page is repainted often - an observation tick, a man moving, a gang
            // stirring. Whatever fell due waits for the hand to let go: the end of the
            // turn raises the flag itself, and nothing is lost because the versions are
            // only marked painted when the paint actually happens.
            if (blockCardModel != null && blockCardModel.Turning)
                return;

            // The caret is in the blueprint's name field. A repaint destroys the field
            // under the player's hands and takes half the typed word with it, so the paint
            // waits the way it waits for the block being turned.
            if (blueprintTyping)
                return;

            var wireVersion = WireBook.Version(outfit);
            if (dirty || paintedWireVersion != wireVersion || paintedVersion != director.Version ||
                paintedOutfitVersion != outfitVersion ||
                paintedGangVersion != Gangs.GangRegistry.Version ||
                paintedTerritoryVersion != territoryVersion ||
                paintedTerritoryObservationVersion != territoryObservationVersion ||
                paintedRacketVersion != racketVersion ||
                paintedExposure != exposure)
            {
                paintedWireVersion = wireVersion;
                paintedVersion = director.Version;
                paintedOutfitVersion = outfitVersion;
                paintedGangVersion = Gangs.GangRegistry.Version;
                paintedTerritoryVersion = territoryVersion;
                paintedTerritoryObservationVersion = territoryObservationVersion;
                paintedRacketVersion = racketVersion;
                dirty = false;
                Repaint();
                // Read AFTER the paint: painting the block file exposes the plate, and a
                // plate counted before that would send the sheet round again for a
                // photograph it had just taken itself.
                paintedExposure = blockCardId.IsValid ? BlockFilm.Get().Exposures : -1;
            }
        }

        /// <summary>Repaints the page that is showing. Each page owns its own rebuild;
        /// the rail, the telex strip and the footer are re-read on every pass because
        /// they are cheap and every page shows the same three.</summary>
        void Repaint()
        {
            switch (currentPage)
            {
                case LedgerPage.Wire:
                    wireSheet.Refresh(outfit);
                    break;
                case LedgerPage.Newspaper:
                    RebuildNewspaper();
                    break;
                case LedgerPage.Personnel:
                    RebuildList();
                    RebuildDetail();
                    break;
                case LedgerPage.Command:
                    RebuildCommand();
                    break;
                case LedgerPage.Blocks:
                    RebuildBlocks();
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
                case LedgerPage.Law:
                    RebuildLaw();
                    break;
                case LedgerPage.Orders:
                    RebuildOrders();
                    break;
                case LedgerPage.Blueprint:
                    RebuildBlueprint();
                    break;
            }
            RefreshRail();
            RefreshTelex();
            RefreshFooter();
            RefreshFilterTapes();
            RebuildTradePopup();
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

        /// <summary>Open the book where the boss left it - what the P key does, and what
        /// the street HUD's ledger key asks for. Public because the key is not on this
        /// canvas; the page it lands on is still the book's own business.</summary>
        public void Open() => OpenAtPage(lastTab);

        /// <summary>The morning sheet's P-key continuation.</summary>
        public void OpenPaper()
        {
            // The loose morning sheet always continues into that same newly delivered
            // edition, even if the archive was left open on an older day.
            newsEditionDay = -1;
            OpenAtPage(LedgerPage.Newspaper);
        }

        /// <summary>
        /// Opens the same modal folder at a specific leaf. Normal P-key entry reopens on
        /// the tab the book was left on; map targeting names its own working page.
        /// </summary>
        void OpenAtPage(LedgerPage pageKind)
        {
            if (!page || director.Roster == null)
                return;

            if (!IsOpen)
                AcquireLedgerPause();

            if (pageKind != LedgerPage.Blocks && BlocksTargetingActive)
            {
                StopBlocksTargeting();
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
            CloseTradePopup();
            if (page)
                page.SetActive(false);
            IsOpen = false;
            ReleaseLedgerPause();
            RestoreOtherCanvases();
            DismissOrganizationTransient();
            DismissBlocksTransient();
            DismissCommandDossier();
            RefreshTargeting();
            MapTargeting.Surface?.SetTargetHighlights(null, Color.clear);
            lastCloseFrame = Time.frameCount;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            LawSheetClosed();
            HideHoverNote();
            if (sortMenu)
                sortMenu.SetActive(false);
        }

        /// <summary>Play-stop or a scene torn down with the book open: the static flag
        /// would otherwise keep every world-input reader standing down in the next
        /// scene, and the map would keep sending clicks to a page that is gone.</summary>
        void OnDestroy()
        {
            CloseTradePopup();
            if (Instance == this)
                Instance = null;
            ReleaseLedgerPause();
            RestoreOtherCanvases();
            IsOpen = false;
            StopBlocksTargeting();
            StopOrderTargeting();
            DismissOrganizationTransient();
            DismissBlocksTransient();
            DismissCommandDossier();
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
            // The Personnel leaf has been retired. Keep its enum slot stable for the
            // later page IDs, but send any stale caller to the sheet that owns dossiers.
            if (pageKind == LedgerPage.Personnel)
                pageKind = LedgerPage.Command;

            if (pageKind != LedgerPage.Command && commandDossierId >= 0)
                DismissCommandDossier();

            if (currentPage != pageKind)
                CloseTradePopup();
            currentPage = pageKind;
            if (System.Array.IndexOf(TabPages, pageKind) >= 0)
                lastTab = pageKind;
            // THE BLUEPRINT IS A POPUP OVER A PAGE, so the page it was opened over stays
            // standing behind its backdrop - the reader has to see the block file they
            // came from, dimmed, exactly as the design shows it.
            for (var i = 0; i < pageRoots.Length; i++)
                if (pageRoots[i])
                    pageRoots[i].SetActive(i == (int)pageKind ||
                        (pageKind == LedgerPage.Blueprint && i == (int)blueprintReturn));

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
            if (pageKind != LedgerPage.Command)
                DismissOrganizationTransient();
            if (pageKind != LedgerPage.Blocks)
                DismissBlocksTransient();

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

            var scroll = mouse.scroll.ReadValue();
            var wheel = scroll.y;
            var point = mouse.position.ReadValue();
            if (blockCardPick.IsValid)
            {
                ScrollTradePopup(wheel, point);
                return;
            }

            // A sideways notch - a trackpad's second axis, or a wheel that tilts - means
            // one thing on this book: pan the chain of command's tree. It is the only
            // region that reads across.
            if (scroll.x != 0f && currentPage == LedgerPage.Command &&
                commandTreeWindow && CommandPanReach() > 0f &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    commandTreeWindow, point))
            {
                commandPan = Mathf.Clamp(commandPan + scroll.x * WheelStep, 0f,
                    CommandPanReach());
                ApplyCommandPan();
                return;
            }

            // And THE LAW's map and its two strips, which read across as a matter of
            // course: a mind map is drawn wider than any window, and a strip of files
            // has no other axis at all.
            if (scroll.x != 0f && currentPage == LedgerPage.Law &&
                ScrollLawAcross(scroll.x, point))
                return;

            if (wheel == 0f)
                return;

            // The rail stands on EVERY page, so its wire is asked first: the pointer over
            // the wire is reading the wire, whichever page is open behind it.
            if (railWireViewport && railWireRun && RectTransformUtility
                    .RectangleContainsScreenPoint(railWireViewport, point))
            {
                var run = Mathf.Max(0f,
                    railWireRun.sizeDelta.y - railWireViewport.rect.height);
                railWireScroll = Mathf.Clamp(railWireScroll - wheel * WheelStep, 0f, run);
                railWireRun.anchoredPosition = new Vector2(0f, railWireScroll);
                return;
            }

            if (currentPage == LedgerPage.Wire)
            {
                wireSheet.Scroll(wheel, point);
                return;
            }

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
                case LedgerPage.Command:
                    if (CommandDossierOpen)
                    {
                        if (!Over(commandDossierViewport, point))
                            return;
                        viewport = commandDossierViewport;
                        content = commandDossierContent;
                    }
                    else
                    {
                        viewport = commandViewport;
                        content = commandContent;
                    }
                    break;
                case LedgerPage.Blocks:
                    // THREE regions on this sheet - the ledger column, the drawer's tab
                    // body, and whatever list a picker has open over it - and whichever
                    // the pointer sits over takes the wheel. The picker is asked first:
                    // it is laid over the tab body and the tab body is still under it.
                    if (blockSheetViewport && Over(blockSheetViewport, point))
                    {
                        viewport = blockSheetViewport;
                        content = blockSheetContent;
                    }
                    else if (blockTabViewport && Over(blockTabViewport, point))
                    {
                        viewport = blockTabViewport;
                        content = blockTabContent;
                    }
                    else
                    {
                        viewport = blocksViewport;
                        content = blocksContent;
                    }
                    break;
                case LedgerPage.Blueprint:
                    viewport = blueprintViewport;
                    content = blueprintContent;
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
                case LedgerPage.Law:
                    // FOUR REGIONS on this sheet, and whichever the pointer sits over
                    // takes the wheel - the PERSONNEL and ARMORY rule. Two of them read
                    // SIDEWAYS off the same notch, so the sheet answers for itself
                    // instead of nominating one region to the tail below.
                    ScrollLaw(wheel, point);
                    return;
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
            else if (viewport == stockViewport)
            {
                stockScroll = Mathf.Clamp(stockScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, stockScroll);
            }
            else if (viewport == commandViewport)
            {
                // The wheel scrolls the SHEET, everywhere on the sheet, including over
                // the tree. The tree is the one thing here that reads across, but it
                // stands at the top of the page and grows as files are opened, so a
                // tree that took the plain notch could cover the whole window and leave
                // the page with no vertical scroll at all. Held with shift - and by a
                // sideways notch, handled above - the same wheel pans the tree.
                var reach = CommandPanReach();
                var overTree = reach > 0f && commandTreeWindow &&
                    RectTransformUtility.RectangleContainsScreenPoint(
                        commandTreeWindow, point);
                var keys = Keyboard.current;
                var shift = keys != null && keys.shiftKey.isPressed;
                if (overTree && shift)
                {
                    commandPan = Mathf.Clamp(
                        commandPan - wheel * WheelStep, 0f, reach);
                    ApplyCommandPan();
                }
                else
                {
                    commandScroll = Mathf.Clamp(
                        commandScroll - wheel * WheelStep, 0f, maxScroll);
                    content.anchoredPosition = new Vector2(0f, commandScroll);
                }
            }
            else if (viewport == commandDossierViewport)
            {
                commandDossierScroll = Mathf.Clamp(
                    commandDossierScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, commandDossierScroll);
                HideHoverNote();
            }
            else if (viewport == blocksViewport)
            {
                blocksScroll = Mathf.Clamp(
                    blocksScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, blocksScroll);
                // The marks are toggled, never repainted: a sheet rebuild per notch
                // would re-read the open block and re-film it for two words.
                ShowScrollMarks(blocksMoreAbove, blocksMoreBelow, blocksScroll,
                    maxScroll);
            }
            else if (viewport == blockTabViewport)
            {
                blockTabScroll = Mathf.Clamp(
                    blockTabScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, blockTabScroll);
                ShowScrollMarks(blockTabMoreAbove, blockTabMoreBelow, blockTabScroll,
                    maxScroll);
            }
            else if (viewport == blockSheetViewport)
            {
                blockSheetScroll = Mathf.Clamp(
                    blockSheetScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, blockSheetScroll);
                ShowScrollMarks(blockSheetMoreAbove, blockSheetMoreBelow,
                    blockSheetScroll, maxScroll);
            }
            else if (viewport == blueprintViewport)
            {
                blueprintScroll = Mathf.Clamp(
                    blueprintScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, blueprintScroll);
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

        /// <summary>
        /// Stand the book on whatever canvas it was given.
        ///
        /// Every page is drawn against a frame of at least 1920x1080 - the design's
        /// sheet, and the floor <see cref="ReferenceFrame"/> clamps to - but the canvas
        /// rect can come back SMALLER than that: an editor game view scaled up, a window
        /// narrower than the reference, a display that reports its size in points. The
        /// book then hangs over the edges, and it is the widest page that pays: the
        /// wire's register loses its last column off the right and its footer off the
        /// foot, with nothing on screen to say so.
        ///
        /// So the whole book is scaled about the canvas centre to fit. Nothing is
        /// re-laid and no page measures anything different; the sheet simply prints
        /// smaller, at the proportions it was drawn in.
        /// </summary>
        void FitPage()
        {
            if (!page || !canvas)
                return;
            var rect = ((RectTransform)canvas.transform).rect;
            if (rect.width < 1f || rect.height < 1f)
                return;
            var fit = Mathf.Min(1f, Mathf.Min(rect.width / FrameW, rect.height / FrameH));
            var scale = page.transform.localScale;
            if (Mathf.Abs(scale.x - fit) > 0.0005f)
                page.transform.localScale = new Vector3(fit, fit, 1f);
        }

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
            wireSheet.Build(NewPageRoot(paper, LedgerPage.Wire), SheetW, SheetH,
                OpenWireItem, WireTargetTrouble);
            BuildBlocksPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildDiplomacyPage(paper);
            BuildCommandPage(paper);
            BuildLawPage(paper);
            BuildOrdersPage(paper);
            BuildBlueprintPage(paper);

            SetPage(currentPage);
            FitPage();

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

        /// <summary>Whether the pointer sits over one of a sheet's regions - which is
        /// what decides who takes the wheel on a page with several.</summary>
        static bool Over(RectTransform rect, Vector2 point) =>
            rect && RectTransformUtility.RectangleContainsScreenPoint(rect, point);

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

        /// <summary>Six speed rungs and HOLD, immediately beside CLOSE.</summary>
        const float ClockReadoutW = 76f;
        const float TimeControlW = 52f;
        const int TimeControlCount = 7;
        const float TimeControlsW = TimeControlW * TimeControlCount;
        const float TimeStripW = ClockReadoutW + TimeControlsW;

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
            BuildTimeControls(chromeRoot);
            BuildClose(chromeRoot);
        }

        /// <summary>The tabs, packed left from the rail's edge. Masked, so a window
        /// too narrow for the strip clips the last word instead of running it over the
        /// way out.</summary>
        void BuildTabs(RectTransform chrome)
        {
            tabStrip = NewRect("Tabs", chrome);
            PlaceTopLeft(tabStrip, RailW, 0f,
                Mathf.Max(0f, FrameW - RailW - CloseW - TimeStripW), ChromeH);
            tabStrip.gameObject.AddComponent<RectMask2D>();

            var naturalWidth = 0f;
            foreach (var name in TabNames) naturalWidth += TabWidthFor(name);
            var padCut = Mathf.Max(0f, naturalWidth - tabStrip.sizeDelta.x) / TabNames.Length;
            var x = 0f;
            for (var i = 0; i < TabNames.Length; i++)
            {
                var kind = TabPages[i];
                var w = TabWidthFor(TabNames[i]) - padCut;
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

        void BuildTimeControls(RectTransform chrome)
        {
            var labels = new[] { "0.5x", "1x", "2x", "4x", "8x", "16x", "HOLD" };
            var group = NewRect("Time Controls", chrome);
            PlaceTopLeft(group, FrameW - CloseW - TimeStripW, 0f,
                TimeStripW, ChromeH);
            VRule(group, 0f, 0f, ChromeH, LedgerStyle.ChromeRule);

            chromeClock = Text("Clock", group, LedgerStyle.MonoBold, 12f,
                LedgerStyle.RailGold, TextAlignmentOptions.Center);
            PlaceTopLeft(chromeClock.rectTransform, 0f, 0f, ClockReadoutW, ChromeH);
            chromeClock.characterSpacing = 5f;
            chromeClock.text = "--:--";
            VRule(group, ClockReadoutW, 0f, ChromeH, LedgerStyle.ChromeRule);

            for (var i = 0; i < TimeControlCount; i++)
            {
                var rung = i;
                var rect = NewRect("Time " + labels[i], group);
                PlaceTopLeft(rect, ClockReadoutW + i * TimeControlW, 0f,
                    TimeControlW, ChromeH);

                var face = rect.gameObject.AddComponent<Image>();
                face.sprite = null;
                face.color = LedgerStyle.Chrome;
                face.raycastTarget = true;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                var colours = button.colors;
                colours.normalColor = Color.white;
                colours.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
                colours.selectedColor = colours.highlightedColor;
                colours.pressedColor = new Color(0.78f, 0.78f, 0.78f);
                button.colors = colours;
                button.onClick.AddListener(() => PickTimeControl(rung));

                var label = Text("Label", rect, LedgerStyle.Mono, 10f,
                    LedgerStyle.TabIdle, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.characterSpacing = 5f;
                label.text = labels[i];

                timeControlFaces[i] = face;
                timeControlLabels[i] = label;
                timeControlButtons[i] = button;
            }

            RefreshTimeControls();
        }

        void AcquireLedgerPause()
        {
            // The book opening is the moment to look again: a scene that has gained a
            // clock since the last time gets one search here, and the per-frame refresh
            // under it goes back to keeping the answer.
            clockSearched = false;
            if (!cityClock)
                cityClock = FindAnyObjectByType<Ambient.CityClock>();
            if (!cityClock)
                return;

            clockWasPaused = cityClock.Paused;
            clockChangedInLedger = false;
            ownsLedgerPause = true;
            cityClock.Paused = true;
            RefreshTimeControls();
        }

        void ReleaseLedgerPause()
        {
            if (!ownsLedgerPause)
                return;

            if (cityClock && !clockChangedInLedger)
                cityClock.Paused = clockWasPaused;
            ownsLedgerPause = false;
        }

        void PickTimeControl(int rung)
        {
            if (!cityClock)
                return;

            clockChangedInLedger = true;
            if (rung >= cityClock.SpeedCount)
            {
                cityClock.Paused = true;
            }
            else
            {
                cityClock.SetSpeed(rung);
                cityClock.Paused = false;
            }
            RefreshTimeControls();
        }

        void RefreshTimeControls()
        {
            // This runs every frame the book is open. The scene is searched ONCE and
            // the answer is kept even when it is "there is no clock here" - the
            // standalone ledger scene has none, and a per-frame FindAnyObjectByType
            // walking the whole scene to learn that again is the worst kind of nothing.
            if (!cityClock && !clockSearched)
            {
                cityClock = FindAnyObjectByType<Ambient.CityClock>();
                clockSearched = true;
            }

            // Written in place, like the rail's own clock two hundred lines below.
            // CityClock.Display builds a string, and a string a frame for a face that
            // changes a digit a minute is an allocation the book does not need.
            if (chromeClock)
            {
                if (cityClock)
                {
                    var hour = cityClock.Hour;
                    chromeClock.SetText("{0:00}:{1:00}", Mathf.FloorToInt(hour),
                        Mathf.FloorToInt(hour % 1f * 60f));
                }
                else
                {
                    chromeClock.SetText("--:--");
                }
            }

            if (!cityClock)
            {
                for (var i = 0; i < timeControlButtons.Length; i++)
                    if (timeControlButtons[i])
                        timeControlButtons[i].interactable = false;
                return;
            }

            var selected = cityClock.Paused ? cityClock.SpeedCount : cityClock.SpeedIndex;
            for (var i = 0; i < timeControlFaces.Length; i++)
            {
                if (!timeControlFaces[i])
                    continue;
                timeControlButtons[i].interactable = true;
                var active = i == selected;
                timeControlFaces[i].color = active
                    ? LedgerStyle.TabRed
                    : LedgerStyle.Chrome;
                timeControlLabels[i].color = active
                    ? LedgerStyle.TabActiveText
                    : LedgerStyle.TabIdle;
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
                // While the blueprint stands over a page, that page's tab stays lit: the
                // reader has not left it.
                var showing = currentPage == LedgerPage.Blueprint
                    ? blueprintReturn : currentPage;
                var active = TabPages[i] == showing;
                tabFaces[i].color = active ? LedgerStyle.TabRed : LedgerStyle.Chrome;
                tabLabels[i].color = active
                    ? LedgerStyle.TabActiveText
                    : LedgerStyle.TabIdle;
            }
        }

        // ------------------------------------------------------------ the status rail

        /// <summary>How many readouts head the rail. The design's five.</summary>
        const int RailTiles = 5;

        /// <summary>The two capacity meters under THE OUTFIT.</summary>
        const int RailMeters = 2;

        const float RailPad = 20f;
        const float RailInner = RailW - RailPad * 2f;
        const float RailTileH = 90f;
        const float RailFigureW = 96f;
        const float RailPayrollH = 74f;

        // The rail's own type, all of it 30% up from the design's drawing. Named here
        // rather than typed at forty call sites, because the one thing that must stay
        // true of this column is that a label is a label wherever it appears on it.
        const float RailKickerSize = 11.7f;   // the panel heads and the tile labels
        const float RailValueSize = 23f;      // the five figures at the head of the rail
        const float RailNoteSize = 12.35f;    // the line of plain English under a figure
        const float RailRowSize = 13f;        // a named row inside a panel
        const float RailRowValueSize = 16.25f;
        const float RailMeterSize = 16.9f;    // the figure over a capacity trough
        const float RailPayrollSize = 22.1f;

        /// <summary>The pitch of one slip on the wire's head - a line box and the
        /// stamp over it.</summary>
        const float RailStampStep = 17f;

        /// <summary>The capacity trough under a rail meter.</summary>
        const float RailTroughH = 7f;

        /// <summary>The band that heads the wire and carries its count.</summary>
        const float RailWireHeadH = 26f;

        /// <summary>One pip of a tile's meter, and the pitch of the run. The run is held
        /// to the right margin of the figure's own line, so it grows with the type or it
        /// reads as a strip of dust beside a 27 point figure.</summary>
        const float RailPipW = 6.5f;
        const float RailPipPitch = 9f;

        readonly TMP_Text[] hudValue = new TMP_Text[RailTiles];
        readonly TMP_Text[] hudNote = new TMP_Text[RailTiles];
        readonly RectTransform[] hudMeter = new RectTransform[RailTiles];

        readonly TMP_Text[] railMeterLabel = new TMP_Text[RailMeters];
        readonly TMP_Text[] railMeterText = new TMP_Text[RailMeters];
        readonly RectTransform[] railMeterFill = new RectTransform[RailMeters];
        readonly Image[] railMeterInk = new Image[RailMeters];

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
        ///
        /// The column is laid against a budget. At the 1080 the canvas is never smaller
        /// than, the rail is 1036 tall: 5 tiles at 90 = 450 and the outfit 100 are laid
        /// at a cursor, the payroll is pinned to the foot at 74, and the wire takes
        /// everything between them - 412 at 1080 and more on a taller screen. Only the
        /// wire stretches, so anything else added up here comes out of the boss's reading
        /// of his own campaign.
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

                var label = Caps(tile, RailPad, -6f, RailInner, RailLabels[i],
                    RailKickerSize, LedgerStyle.RailLabel, 13f);
                label.overflowMode = TextOverflowModes.Ellipsis;

                hudValue[i] = Line(tile, LedgerStyle.Condensed, RailValueSize,
                    LedgerStyle.RailValue, RailPad, -24f, RailInner - RailFigureW, 32f, "");
                hudValue[i].characterSpacing = 1f;
                hudValue[i].overflowMode = TextOverflowModes.Ellipsis;

                // The pips sit on the figure's own line, held to the right margin -
                // the design's meter is a reading OF the figure, not a bar under it.
                var meter = NewRect("Meter", tile);
                PlaceTopLeft(meter, RailPad, -24f, RailInner, 32f);
                hudMeter[i] = meter;

                // Two lines of room. The wider column takes every note but the tribute
                // man's on one, and that one is the reason the second line stays: a
                // note that clips is not a note.
                hudNote[i] = Paragraph(tile, LedgerStyle.Mono, RailNoteSize,
                    LedgerStyle.RailNote, RailPad, -57f, RailInner, 30f, "",
                    lineSpacing: 0f);
                hudNote[i].overflowMode = TextOverflowModes.Ellipsis;

                cursor += RailTileH;
            }

            railClock = hudNote[0];
            cursor = BuildRailOutfit(cursor);
            BuildRailWire(cursor);
            BuildRailPayroll();
        }

        float BuildRailOutfit(float cursor)
        {
            var height = 28f + RailMeters * 34f + 4f;
            var panel = NewRect("The Outfit", railRoot);
            PlaceTopLeft(panel, 0f, -cursor, RailW, height);
            Rule(panel, 0f, -(height - 1f), RailW, LedgerStyle.ChromeRule);
            Caps(panel, RailPad, -7f, RailInner, "THE OUTFIT", RailKickerSize,
                LedgerStyle.RailKicker, 16f);

            for (var i = 0; i < RailMeters; i++)
            {
                var y = -(28f + i * 34f);
                railMeterLabel[i] = Line(panel, LedgerStyle.Mono, RailNoteSize,
                    LedgerStyle.RailLabel, RailPad, y, RailInner - RailFigureW, 16f, "");
                railMeterLabel[i].characterSpacing = 5f;
                railMeterLabel[i].overflowMode = TextOverflowModes.Ellipsis;

                railMeterText[i] = Line(panel, LedgerStyle.MonoBold, RailMeterSize,
                    LedgerStyle.RailValue, RailPad + RailInner - RailFigureW, y - 1f,
                    RailFigureW, 19f, "", TextAlignmentOptions.MidlineRight);

                var trough = NewRect("Trough", panel);
                PlaceTopLeft(trough, RailPad, y - 21f, RailInner, RailTroughH);
                Fill(trough, LedgerStyle.RailTrough);

                var ink = NewRect("Fill", trough);
                PlaceTopLeft(ink, 0f, 0f, 0f, RailTroughH);
                railMeterInk[i] = Fill(ink, LedgerStyle.RailValue);
                railMeterFill[i] = ink;
            }

            return cursor + height;
        }

        /// <summary>The shared wire archive in the rail's available scrolling space.</summary>
        void BuildRailWire(float cursor)
        {
            railWire = NewRect("The Wire", railRoot);
            railWire.anchorMin = new Vector2(0f, 0f);
            railWire.anchorMax = new Vector2(1f, 1f);
            railWire.pivot = new Vector2(0.5f, 1f);
            railWire.offsetMin = new Vector2(0f, RailPayrollH);
            railWire.offsetMax = new Vector2(0f, -cursor);

            var head = NewRect("Head", railWire);
            PlaceTopLeft(head, 0f, 0f, RailW, RailWireHeadH);
            Caps(head, RailPad, -7f, RailInner, "THE WIRE", RailKickerSize,
                LedgerStyle.RailKicker, 16f);
            railWireCount = Caps(head, RailPad, -7f, RailInner - 132f, "", 11f,
                LedgerStyle.RailLabel, 6f, TextAlignmentOptions.MidlineRight);
            railWireCount.font = LedgerStyle.Mono;

            // THIS BLOCK / ALL. The pair only means anything while a block file is open,
            // so it only stands then - and an incident, which happened to men and not at
            // an address, has no block and drops out under THIS BLOCK by having none.
            railWireScopeThis = NewRect("Scope this", head);
            PlaceTopLeft(railWireScopeThis, RailPad + RailInner - 128f, -5f, 66f, 17f);
            LedgerV2.Button(railWireScopeThis, "THIS BLOCK", 0f, 0f, 66f, 17f,
                () => { railWireThisBlock = true; railWirePainted = -1; },
                LedgerV2.Key.Ghost, 8f);
            railWireScopeAll = NewRect("Scope all", head);
            PlaceTopLeft(railWireScopeAll, RailPad + RailInner - 58f, -5f, 58f, 17f);
            LedgerV2.Button(railWireScopeAll, "ALL", 0f, 0f, 58f, 17f,
                () => { railWireThisBlock = false; railWirePainted = -1; },
                LedgerV2.Key.Ghost, 8f);

            Rule(head, RailPad, -(RailWireHeadH - 1f), RailInner, LedgerStyle.RailHair);

            railWireViewport = NewRect("Viewport", railWire);
            railWireViewport.anchorMin = new Vector2(0f, 0f);
            railWireViewport.anchorMax = new Vector2(1f, 1f);
            railWireViewport.pivot = new Vector2(0.5f, 1f);
            railWireViewport.offsetMin = Vector2.zero;
            railWireViewport.offsetMax = new Vector2(0f, -RailWireHeadH);
            railWireViewport.gameObject.AddComponent<RectMask2D>();

            // The wire's own floor, in rail stock: the run scrolls under the payroll
            // block and the outfit panel, and a slip sliding out of the viewport has to
            // meet the column rather than whatever is behind the mask.
            var floor = railWireViewport.gameObject.AddComponent<Image>();
            floor.color = LedgerStyle.Rail;
            floor.raycastTarget = false;

            railWireRun = NewRect("Run", railWireViewport);
            railWireRun.anchorMin = new Vector2(0f, 1f);
            railWireRun.anchorMax = new Vector2(1f, 1f);
            railWireRun.pivot = new Vector2(0.5f, 1f);
            railWireRun.anchoredPosition = Vector2.zero;
            railWireRun.sizeDelta = Vector2.zero;

            railWireScroll = 0f;
            railWirePainted = -1;
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

            Caps(pay, RailPad, -7f, RailInner, "PAYROLL RUNNING", RailKickerSize,
                LedgerStyle.RailLabel, 13f);
            railPayroll = Line(pay, LedgerStyle.Condensed, RailPayrollSize,
                LedgerStyle.RailRed, RailPad, -25f, RailInner, 30f, "");
            railPayrollNote = Line(pay, LedgerStyle.Mono, RailNoteSize,
                LedgerStyle.RailNote, RailPad, -56f, RailInner, 16f,
                "paid at midnight, worked or not");
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
            RefreshRailWire();

            var perDay = Outfit.Wages.DailyPayroll(director.Roster);
            if (railPayroll)
                railPayroll.text = LedgerText.Cash(perDay) + " / day";

            // WAGE-003. A short night is not a quiet one: the rail says how many men
            // are standing there unpaid before the player has to go looking for it.
            var unpaid = Outfit.Wages.UnpaidCount(director.Roster);
            if (railPayrollNote)
            {
                railPayrollNote.text = unpaid > 0
                    ? "SHORT " + LedgerText.Cash(
                          Outfit.Wages.UnpaidWages(director.Roster)) + " · " +
                      unpaid + (unpaid == 1 ? " man unpaid" : " men unpaid")
                    : perDay > 0
                        ? "paid at midnight, worked or not"
                        : "nobody is drawing pay";
                railPayrollNote.color = unpaid > 0
                    ? LedgerStyle.RailRed
                    : LedgerStyle.RailNote;
            }
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
                outfit.CollectKnownHoldings(holdings);
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
        }

        /// <summary>Rebuild the rail only when its shared book or scope changes.</summary>
        void RefreshRailWire()
        {
            if (railWireRun == null)
                return;

            var filed = WireBook.Count(outfit);
            if (railWireCount)
                railWireCount.text = filed == 0 ? "QUIET" : filed + " FILED";

            // The pair only stands while there is a block to narrow to.
            var scoped = blockCardId.IsValid;
            if (railWireScopeThis)
                railWireScopeThis.gameObject.SetActive(scoped);
            if (railWireScopeAll)
                railWireScopeAll.gameObject.SetActive(scoped);
            if (!scoped)
                railWireThisBlock = false;

            var version = WireBook.Version(outfit);
            if (version == railWirePainted && blockCardId == railWireBlock)
                return;
            railWirePainted = version;
            railWireBlock = blockCardId;

            for (var i = railWireRun.childCount - 1; i >= 0; i--)
                Destroy(railWireRun.GetChild(i).gameObject);

            WireBook.Collect(outfit, railWireLines);
            if (railWireThisBlock && scoped)
                for (var i = railWireLines.Count - 1; i >= 0; i--)
                    if (railWireLines[i].BlockId != blockCardId)
                        railWireLines.RemoveAt(i);

            var y = 0f;
            if (railWireLines.Count == 0 && railWireThisBlock && scoped)
            {
                y = LayWireSlip(y, new WireLine("WIRE",
                    "DAY " + (outfit ? outfit.Campaign.Day : 1),
                    "Nothing has come off this block.", "", "",
                    LedgerStyle.TelexPlain, outfit ? outfit.Campaign.Day : 1));
            }
            else if (railWireLines.Count == 0)
            {
                // A wire with nothing on it reads as a machine that has failed, not a
                // quiet night - the strip over the sheet says the same thing in the same
                // words on the same night.
                y = LayWireSlip(y, new WireLine("WIRE",
                    "DAY " + (outfit ? outfit.Campaign.Day : 1),
                    "Nothing on the wire. Nobody of ours has done a thing he was not " +
                    "told to.", "", "", LedgerStyle.TelexPlain,
                    outfit ? outfit.Campaign.Day : 1));
            }
            else
            {
                for (var i = 0; i < railWireLines.Count; i++)
                    y = LayWireSlip(y, railWireLines[i]);
            }

            railWireRun.sizeDelta = new Vector2(0f, -y);

            // A slip landing while the boss is reading day one must not throw him back
            // to the top, but the run he is scrolled into may have got shorter.
            var max = Mathf.Max(0f, -y - railWireViewport.rect.height);
            railWireScroll = Mathf.Clamp(railWireScroll, 0f, max);
            railWireRun.anchoredPosition = new Vector2(0f, railWireScroll);
        }

        /// <summary>A measured slip; every filed item opens its current actions.</summary>
        float LayWireSlip(float y, WireLine line)
        {
            const float EdgeW = 3f;
            const float CopyX = RailPad + EdgeW + 8f;
            var copyW = RailInner - EdgeW - 8f;

            var body = Paragraph(railWireRun, LedgerStyle.Mono, RailNoteSize,
                LedgerStyle.RailNote, CopyX, y - RailStampStep, copyW, 0f, line.Body,
                lineSpacing: 0f);
            var tall = Mathf.Ceil(body.GetPreferredValues(line.Body, copyW, 0f).y);
            body.rectTransform.sizeDelta = new Vector2(copyW, tall);

            var height = RailStampStep + tall + 9f;

            var edge = NewRect("Edge", railWireRun);
            PlaceTopLeft(edge, RailPad, y - 3f, EdgeW, height - 6f);
            Fill(edge, line.Ink);

            var stamp = Caps(railWireRun, CopyX, y - 1f, copyW,
                line.Tag.Length > 0 ? line.Stamp + " · " + line.Tag : line.Stamp,
                11f, line.Ink, 8f);
            stamp.font = LedgerStyle.MonoBold;
            stamp.overflowMode = TextOverflowModes.Ellipsis;

            if (line.Figure.Length > 0)
                Caps(railWireRun, CopyX, y - 1f, copyW, line.Figure, 11f,
                    LedgerStyle.RailAmber, 6f, TextAlignmentOptions.MidlineRight);

            if (!string.IsNullOrEmpty(line.Tag))
            {
                var surface = NewRect("Slip " + line.ActionLabel, railWireRun);
                PlaceTopLeft(surface, RailPad, y, RailInner, height);
                surface.SetAsFirstSibling();
                RowButton(surface, ClickSurface(surface), () => OpenWireItem(line));
            }

            Rule(railWireRun, RailPad, y - height + 3f, RailInner, LedgerStyle.RailHair);
            return y - height;
        }

        /// <summary>Resolve stable targets at click time; the destination owns admission
        /// and commands. An expired target opens the record with an explanation.</summary>
        public void OpenWireItem(WireLine line)
        {
            var unavailable = WireTargetTrouble(line);
            if (unavailable.Length == 0)
                switch (line.Action)
                {
                    case WireAction.Person:
                        OpenAtPage(LedgerPage.Command);
                        OpenCommandDossier(line.CharacterId);
                        return;
                    case WireAction.Door:
                        if (!IsOpen) OpenAtPage(LedgerPage.Wire);
                        wireSheet.StopTyping();
                        PickTrade(line.BusinessId);
                        return;
                    case WireAction.Block:
                        if (!WireBlockOf(line, out var block))
                            break;
                        OpenAtPage(LedgerPage.Blocks);
                        blocksScroll = 0f;
                        if (blockCardId != block) OpenBlockCard(block);
                        CloseTradePopup();
                        return;
                    case WireAction.Law: OpenAtPage(LedgerPage.Law); return;
                    case WireAction.Finances: OpenAtPage(LedgerPage.Finances); return;
                    case WireAction.Families: OpenAtPage(LedgerPage.Diplomacy); return;
                }
            OpenAtPage(LedgerPage.Wire);
            wireSheet.ShowRecord(line, unavailable);
        }

        /// <summary>
        /// Whether the file a wire line points at is still there, and in what words it
        /// is not.
        ///
        /// The register asks this when the slip is DRAWN rather than when the key is
        /// pressed: a destination that cannot be reached is greyed with its reason
        /// printed beside it, so the reader is told before he presses instead of being
        /// left standing on the page he was already on.
        /// </summary>
        public string WireTargetTrouble(WireLine line)
        {
            switch (line.Action)
            {
                case WireAction.Person:
                    return director?.Roster?.Find(line.CharacterId) == null
                        ? "This man is no longer in the outfit's roster." : "";
                case WireAction.Door:
                    return DoorMenu.TryRead(line.BusinessId, out _)
                        ? "" : "This address is no longer available in the city.";
                case WireAction.Block:
                    return WireBlockOf(line, out _)
                        ? "" : "This address is no longer available in the city.";
                default:
                    return "";
            }
        }

        /// <summary>The block a wire line's address belongs to, resolved from the city
        /// as it stands now rather than from anything the slip remembers.</summary>
        bool WireBlockOf(WireLine line, out TerritoryBlockId block)
        {
            block = line.BlockId;
            var found = line.Action == WireAction.Block;
            if (!found)
                foreach (var row in CityBusinesses.All)
                    if (row.Id == line.BusinessId)
                    { block = row.CanonicalBlockId; found = true; break; }
            var geography = TerritoryRuntime.Instance?.Geography;
            return found && block.IsValid && geography != null &&
                geography.TryGetBlock(block, out _);
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
            railMeterFill[index].sizeDelta = new Vector2(RailInner * fraction, RailTroughH);
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
            var x = RailInner - LedgerV2.PipsWidth(steps, RailPipW, RailPipPitch);
            LedgerV2.Pips(meter, x, -16f, steps, filled, colour, RailPipW, 12f, RailPipPitch);
        }

        // -------------------------------------------------------------- what is true

        // The figures the rail and the telex strip both read. Tallied once a repaint,
        // from the same authorities the pages use - never from a page's own scratch,
        // because the rail is on every page and the pages are not.
        int railMen, railManCap, railHeld, railBlockCap;
        int railPosted, railIdle, railHurt;
        int railContested, railAtWar, railIssued, railStock, railProfit;
        int railOverCapacity, railPaperOnly;
        string railFirstIdle = "";

        void TallyOutfit()
        {
            railMen = railManCap = railHeld = railBlockCap = 0;
            railPosted = railIdle = railHurt = 0;
            railContested = railAtWar = railIssued = railStock = railProfit = 0;
            railOverCapacity = railPaperOnly = 0;
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

                // The stock book counts ITEMS signed out, not men: the wire says how
                // much of the armory is in hands, and a man holding two guns has two of
                // them out.
                railStock = roster.Equipment.Count;
                for (var i = 0; i < roster.Equipment.Count; i++)
                    if (roster.Equipment[i].OwnerId != RosterEquipment.Unheld)
                        railIssued++;
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
                        outfit.StanceWith(gangs[i].Id) == Outfit.Stance.War)
                        railAtWar++;


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
        TerritoryBusinessId telexBusinessTarget;
        string telexBusinessMessage = "";

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
                var target = body == telexBusinessMessage
                    ? telexBusinessTarget
                    : default;
                const float ctaW = 104f;
                var width = pad * 2f + body.Length * (11.5f * 0.6f + 11.5f * 0.02f) +
                    16f + (target.IsValid ? ctaW + 10f : 0f);

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
                    pad + 15f, -8f,
                    width - pad - 15f - (target.IsValid ? ctaW + 10f : 0f), 16f, body);
                text.characterSpacing = 2f;

                if (target.IsValid)
                    LedgerV2.Button(slot, "SHOW STORE", width - ctaW - 10f, -3f,
                        ctaW, TelexH - 6f, () => FocusWireBusiness(target),
                        LedgerV2.Key.Dark, 8.5f);

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
            telexBusinessTarget = default;
            telexBusinessMessage = "";

            if (lastRefusal.Length > 0)
                telexMessages.Add((lastRefusal, TelexVoice.Urgent));

            ComposeBusinessRefusal();

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

            ComposePageTelex();

            // A wire with nothing on it is a machine that has failed, not a quiet night.
            if (telexMessages.Count == 0)
                telexMessages.Add(("Nothing on the wire. The city is quiet and the books " +
                    "are open.", TelexVoice.Plain));
        }

        /// <summary>How many lines of door news the book's own strip carries.</summary>
        const int TelexDoorLines = 3;

        /// <summary>
        /// What happened at our doors, newest first: the answer an owner gave, the front
        /// that went in, the shop that stopped paying. The racket's own dispatches, in
        /// the racket's own words - the strip over the street prints the SAME sentences
        /// from the SAME feed, so the book and the map never report different nights.
        ///
        /// The newest line carries the door's identity, so its CTA can take the boss
        /// straight back to the place instead of merely reporting an unactionable NO.
        /// </summary>
        void ComposeBusinessRefusal()
        {
            var racket = TerritoryRuntime.Instance?.Racket;
            if (racket == null)
                return;

            var dispatches = racket.Dispatches;
            var us = new TerritoryGangId(GangCatalog.PlayerGangId);
            var shown = 0;
            for (var i = dispatches.Count - 1; i >= 0 && shown < TelexDoorLines; i--)
            {
                var dispatch = dispatches[i];
                if (dispatch.GangId != us)
                    continue;

                var name = "THIS STORE";
                var rows = CityBusinesses.All;
                for (var r = 0; r < rows.Count; r++)
                    if (rows[r].Id == dispatch.BusinessId)
                    {
                        if (!string.IsNullOrWhiteSpace(rows[r].Name))
                            name = rows[r].Name;
                        break;
                    }

                var body = TerritoryStandingVocabulary.Default.Describe(dispatch, name, "");
                if (shown == 0)
                {
                    telexBusinessTarget = dispatch.BusinessId;
                    telexBusinessMessage = body;
                }

                telexMessages.Add((body, VoiceOf(dispatch.News)));
                shown++;
            }
        }

        static TelexVoice VoiceOf(TerritoryDoorNews news)
        {
            switch (news)
            {
                case TerritoryDoorNews.Refused:
                case TerritoryDoorNews.StoppedPaying:
                case TerritoryDoorNews.ChangedHands:
                    return TelexVoice.Urgent;
                case TerritoryDoorNews.Wrecked:
                case TerritoryDoorNews.Beaten:
                case TerritoryDoorNews.OwnerBeaten:
                case TerritoryDoorNews.Threatened:
                    return TelexVoice.Warn;
                default:
                    return TelexVoice.Plain;
            }
        }

        void FocusWireBusiness(TerritoryBusinessId businessId)
        {
            if (!CityBusinesses.TryApproachPoint(businessId, out var world))
                return;

            Close();
            var rig = FindAnyObjectByType<RoadDemo.DemoCamera>();
            if (rig)
                rig.Ride(() => world);
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

                case LedgerPage.Command:
                    telexMessages.Add(("Each man answers to exactly one man above him",
                        TelexVoice.Plain));
                    telexMessages.Add(("Click a name and his file opens where he " +
                        "stands · several at once", TelexVoice.Plain));
                    telexMessages.Add(("Nothing on this sheet happens at the click · " +
                        "the order is FILED and the outfit answers it", TelexVoice.Plain));
                    break;

                case LedgerPage.Law:
                    ComposeLawTelex();
                    break;

                case LedgerPage.Blocks:
                    telexMessages.Add((railHeld > 0
                        ? railHeld + (railHeld == 1 ? " block held" : " blocks held") + " on the street"
                        : "The outfit holds no ground at all",
                        railHeld > 0 ? TelexVoice.Plain : TelexVoice.Urgent));
                    telexMessages.Add((railContested > 0
                        ? railContested + (railContested == 1 ? " block" : " blocks") +
                          " contested — another house is pushing on it"
                        : "Nobody is pushing on our ground today",
                        railContested > 0 ? TelexVoice.Warn : TelexVoice.Plain));
                    telexMessages.Add(("What is on our PAPER and what is ours on the " +
                        "STREET are two different columns", TelexVoice.Plain));
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
                    telexMessages.Add(("War is declared; a truce or a peace is offered " +
                        "at the table and lands at midnight", TelexVoice.Plain));
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
            if (currentPage == LedgerPage.Newspaper)
            {
                if (classifiedOpen && classifiedNote.Length > 0)
                    return classifiedNote;
                if (!classifiedOpen && newspaperNote.Length > 0)
                    return newspaperNote;
            }

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
                   " · " + LedgerText.Cash(Outfit.Wages.WageFor(member, RosterDay)) + " a day";
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
            // A TAB-LESS PAGE HAS NO FOLIO. The blueprint is one leaf of a building's
            // own file, not of the book, so it says which building it is instead of a
            // page number the book does not have - and the array is never indexed past
            // its end, which is what an added page did to this line the first time.
            var folio = currentPage == LedgerPage.Wire ? "THE WIRE"
                : (int)currentPage < TabFolios.Length
                ? "PAGE " + TabFolios[(int)currentPage].ToString("00") + " OF " + Folios
                : "THE BLUEPRINT";
            footerRight.text = "[ ] TURN THE PAGE   [ESC] SHUT THE FILE   |   " + folio;
        }
    }
}
