using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace RoadDemo
{
    /// <summary>
    /// The boss's paperwork, laid over the street: the PERSONAL FILE down the left,
    /// THE WIRE down the right, and the key that opens the ledger in the bottom-left
    /// corner.
    ///
    /// It owns no facts. The hour and the speed ladder are DemoClockHud's, the row of
    /// live crew feeds is CrewBar's, the corner plate is TurfMinimap's, and the book is
    /// PersonnelAlmanac. What this class draws, it draws out of systems that were
    /// already standing:
    ///
    ///   the file   TurfMapHud.Units - the same TurfCrew projection the plate reads,
    ///              carrying the lieutenant's rank, his three trades in half steps,
    ///              what he carries and what he rides. Its six orders go through
    ///              TurfMapHud.Order, the one gateway, and its picture is CrewBar's own
    ///              rotating camera feed rather than a second one. It replaces the
    ///              CREWS AFIELD column TurfMapPanel used to keep: that column and the
    ///              crew bar were the same roster printed twice, and the file it opened
    ///              was reachable only from the column.
    ///   the wire   OutfitDirector.Incidents, newest first, as telex slips. Nothing
    ///              here composes a sentence: IncidentText wrote every one of these the
    ///              day it happened, and the paper, the ledger and this strip print the
    ///              same words. The strip the ledger runs is inside the book, so a boss
    ///              on the street had no way to see what his men had just done.
    ///   the key    PersonnelAlmanac, opened the way P opens it - and it prints P,
    ///              because a HUD that names a key the game does not read is a lie on
    ///              the screen.
    ///
    /// It stands up over the turf plate as well as the street, because the file it
    /// carries is the plate's own file: pulling the wheel back must not take the
    /// lieutenant's dossier away.
    ///
    /// Drawn in the ledger's terminal chrome (LedgerV2) rather than DemoUi's navy: both
    /// are the boss's paperwork laid over the street, not the demo's instruments.
    ///
    /// Sorting order 115 - the slot the crew column it replaces held: over the street,
    /// over the turf plate (60), under the book (110 suspends every canvas anyway). It
    /// carries a GraphicRaycaster because its surfaces are pressed, so it answers
    /// <see cref="Contains"/> for the world pickers, exactly as CrewBar does: a click
    /// that worked a key here must not also select a man behind it.
    /// </summary>
    public sealed class StreetHud : MonoBehaviour
    {
        const int SortingOrder = 115;

        // ---- the crew chips ----
        /// <summary>The chip row stands beside the clock strip and matches its height,
        /// so the two read as one bar across the top rather than two panels that happen
        /// to touch. DemoClockHud takes this share of the width for itself.</summary>
        const float BarTall = 62f;
        const float ChipWide = 172f;
        const float ChipPlate = 46f;
        /// <summary>The design's own cap on the top bar: calc(100% - 332px), so a long
        /// row of chips runs out of room before it runs under the wire.</summary>
        const float BarRightMargin = 332f;

        // ---- the file ----
        const float FileWide = 252f;
        /// <summary>Under the clock strip, with the same strip of air the crew column
        /// this replaces left there.</summary>
        const float FileTop = 63f;
        /// <summary>The design stops the file 46 above the floor - the ledger key's own
        /// height and the air over it.</summary>
        const float FileBottomGap = 46f;
        const float FilePad = 8f;
        const float PlateWide = 124f, PlateTall = 84f;
        const int Meter = 10;   // the ledger's half-step scale, and the map's

        // ---- the wire ----
        const float WireWide = 326f;
        const float WireHeadTall = 26f;
        const float SlipTall = 56f;
        const float WireGap = 4f;
        const int WireLines = 4;

        // ---- the ledger key ----
        /// <summary>The width of the left column TurfMapPanel keeps for the crews and
        /// the file: the key closes that column off at the bottom rather than starting
        /// a second one.</summary>
        const float KeyWide = 252f;
        const float KeyTall = 30f;

        public static StreetHud Instance { get; private set; }

        /// <summary>Static state outlives Play when domain reload is off - the same
        /// reset every other layer here keeps.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        Canvas _canvas;
        RectTransform _wireRoot, _keyRoot, _fileRoot;
        bool _wireOpen = true;

        TurfMapHud _hud;
        RectTransform _chipRoot;
        int _paintedChipRoster = -1;

        // What the file is painted against. The plate bumps CrewFileVersion whenever the
        // pick moves; the two live figures under it move without it.
        int _paintedCrewFile = -1;
        int _paintedCrewId = int.MinValue;
        int _paintedStanding = -1;
        TurfOrder _paintedOrder = (TurfOrder)(-1);
        TextMeshProUGUI _menText, _orderText;

        // What is painted, so a HUD over a city running at 4x is not rebuilt every frame
        // for a wire nothing has been added to.
        int _paintedIncidents = -1;
        int _paintedCampaignDay = -1;
        bool _paintedWireOpen;

        readonly List<WireLine> _lines = new List<WireLine>();

        DemoCrews _crews;

        public void Init(DemoCrews crews)
        {
            _crews = crews;

            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[StreetHud] No TMP default font - the wire and the " +
                                 "ledger key are off until TMP essentials are imported.",
                    this);
                enabled = false;
                return;
            }

            Instance = this;
            EnsureEventSystem();
            BuildCanvas();
            PaintLedgerKey();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Whichever layer gets here first brings the pointer stack; the
        /// almanac and the context menu do the same, and none of the three may assume
        /// another one ran.</summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current)
                return;
            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            // Not StandaloneInputModule: the legacy module throws under this project's
            // Input System setting.
            host.AddComponent<InputSystemUIInputModule>();
        }

        void BuildCanvas()
        {
            var root = new GameObject("Street HUD", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 1280 x 720, which is the frame the design's own numbers are in and the
            // one DemoClockHud and TurfMinimap already work in. Laid against 1920 the
            // same numbers come out two thirds the size, which is what made the file a
            // narrow strip beside a design that gives it a fifth of the screen.
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>().alpha = HudNight.Alpha;

            // The design lays a wide vignette over the city before any panel goes down:
            // clear through the middle two fifths and closing to near black at the
            // corners, which is what settles the paper against the street instead of
            // leaving four bright panels floating on a bright picture. First child, so
            // every panel is drawn over it, and it takes no clicks.
            var shade = NewRect("Vignette", root.transform);
            Stretch(shade);
            var wash = shade.gameObject.AddComponent<RawImage>();
            wash.texture = LedgerStyle.Vignette;
            wash.color = new Color(0.016f, 0.016f, 0.031f, 0.55f);
            wash.raycastTarget = false;
            shade.SetAsFirstSibling();

            // The design's vignette is a SIBLING of the map layer, outside the hud
            // region - so it is laid at full strength and the 20% the panels are seeing
            // through is 20% of an already darkened street. Riding inside the group
            // would put the wash at eight tenths as well, and the panels would be
            // reading through onto a brighter city than the design ever shows.
            shade.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;

            // The scaler solves the canvas rect in its own layout pass; force one now so
            // the first paint lays the panels against the real frame.
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// Does this screen point land on one of the panels? The world pickers ask
        /// before they act, the same question they already ask CrewBar - a press that
        /// worked the wire or the ledger key must not also pick a man behind it.
        /// </summary>
        public bool Contains(Vector2 screen) =>
            _canvas != null && _canvas.enabled &&
            (Over(_chipRoot, screen) || Over(_fileRoot, screen) ||
             Over(_wireRoot, screen) || Over(_keyRoot, screen));

        static bool Over(RectTransform rect, Vector2 screen) =>
            rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);

        /// <summary>
        /// Stand down under the strategic map, which is a different city altogether.
        /// NOT under the turf plate: the plate is a zoom level of this same city and the
        /// file is the plate's own file - it went up with the wheel before this class
        /// existed and it still does. The almanac suspends every canvas on its own.
        /// </summary>
        static bool ModalUp => PersonnelAlmanac.IsOpen || StrategicMapHud.IsOpen;

        void Update()
        {
            if (_canvas == null)
                return;

            var want = !ModalUp;
            if (_canvas.enabled != want)
                _canvas.enabled = want;
            if (!want)
                return;

            _night.Relight();
            PaintFileIfMoved();

            var outfit = OutfitDirector.Instance;
            var incidents = outfit != null ? outfit.Incidents.Count : 0;
            var day = outfit != null ? outfit.Campaign.Day : -1;

            if (incidents == _paintedIncidents && day == _paintedCampaignDay &&
                _wireOpen == _paintedWireOpen)
                return;

            _paintedIncidents = incidents;
            _paintedCampaignDay = day;
            _paintedWireOpen = _wireOpen;
            PaintWire(outfit);
        }

        // ------------------------------------------------------------ day and night

        /// <summary>The night pass, shared with the clock strip beside it - the two are
        /// one bar across the top of the screen and must cross together.</summary>
        readonly HudNight _night = new HudNight();

        // --------------------------------------------------------------- crew chips

        /// <summary>Which crews are on the row and how many of each are up. The chips
        /// carry a count, so a man falling has to redraw them even though the roster
        /// itself has not changed.</summary>
        int RosterStamp()
        {
            var stamp = 17;
            var units = _hud.Units;
            for (var i = 0; i < units.Count; i++)
            {
                var crew = units[i];
                if (!crew.Mine)
                    continue;
                stamp = stamp * 31 + crew.Id;
                stamp = stamp * 31 + crew.MenStanding;
            }
            return stamp;
        }

        /// <summary>
        /// The outfit across the top, one chip to a lieutenant: his live picture, his
        /// name, what he is doing and how many of him are standing. Pressing a chip is
        /// the same pick as clicking the man himself - TurfMapHud.SelectOnly, which
        /// tells the street too - so the row and the city never disagree about who is
        /// picked.
        ///
        /// This is the roster the CREWS AFIELD column used to keep. The column was a
        /// second list of the same crews in a second place, and the file it opened could
        /// only be reached through it; the row keeps the list where the eye already
        /// goes, beside the clock, and the file opens beneath it.
        /// </summary>
        void PaintChips(TurfCrew picked)
        {
            if (_chipRoot != null)
                Destroy(_chipRoot.gameObject);
            _night.ForgetDead();

            var root = NewRect("Crew chips", _canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(ChipRowLeft(), 0f);
            root.sizeDelta = new Vector2(0f, BarTall);
            _chipRoot = root;

            // The design caps the bar at calc(100% - 332px) and hides the overflow, so
            // the crews stop rather than sliding in under the wire.
            var frame = ((RectTransform)_canvas.transform).rect.width;
            var room = Mathf.Max(0f, frame - BarRightMargin - ChipRowLeft());

            var x = 0f;
            var units = _hud.Units;
            for (var i = 0; i < units.Count; i++)
            {
                var crew = units[i];
                if (!crew.Mine)
                    continue;
                if (x + ChipWide > room)
                    break;
                Chip(root, x, crew, crew == picked);
                x += ChipWide;
            }


            root.sizeDelta = new Vector2(x, BarTall);
            if (x <= 0f)
            {
                Destroy(root.gameObject);
                _chipRoot = null;
                return;
            }
            _night.Register(root);
        }

        /// <summary>
        /// Where the chip row starts: hard against the clock strip's right edge. The
        /// two canvases are scaled to the same 1280 x 720 frame, so the strip's own
        /// width is a width over here too, and the design's bar is continuous - the
        /// chips butt onto the clock rather than floating clear of it.
        /// </summary>
        float ChipRowLeft() => DemoClockHud.PlateWidth;

        void Chip(RectTransform row, float x, TurfCrew crew, bool picked)
        {
            var chip = NewRect("Chip " + crew.Id, row);
            PlaceTopLeft(chip, x, 0f, ChipWide, BarTall);
            Fill(chip, picked ? LedgerV2.Picked : LedgerV2.Panel);
            // Left picks him, right puts the camera on him - the two clicks the crew
            // column answered at every zoom level, kept on the chip that replaced it.
            var clicks = ClickSurface(chip).gameObject.AddComponent<ChipClicks>();
            clicks.Pick = () => _hud.SelectOnly(crew);
            clicks.Ride = () => _hud.Focus(crew);

            // The picked chip is struck down its left edge in red. The edge is THERE on
            // every chip and merely colourless when the crew is not picked - drawing it
            // only on the picked one would shift the picture and the name three units
            // sideways every time the pick moved.
            Block("Edge", chip, 0f, 0f, 3f, BarTall,
                picked ? LedgerV2.Red : new Color(0f, 0f, 0f, 0f));
            Block("Divider", chip, ChipWide - 1f, 0f, 1f, BarTall, LedgerV2.Rule);

            // Flush: the design's plate fills the chip's height with no border and no
            // shadow, and the picture covers it. It is a window cut in the chip, not a
            // photograph laid on one.
            var plate = LedgerV2.PortraitPlate(chip, 3f, 0f, ChipPlate, BarTall, "",
                LedgerV2.DarkPlate);
            Picture(plate, crew);

            var textX = 3f + ChipPlate + 9f;
            var textW = ChipWide - textX - 12f;
            // Three lines in sixty-two units. TMP's line box is far taller than the
            // type in it, so the boxes are allowed to overlap and it is the CENTRES
            // that are spaced - stacking the boxes end to end would need seventy-two
            // and push the count off the bottom of the bar.
            var name = LedgerV2.Name(chip, textX, -2f, textW, crew.Name, 15f);
            name.raycastTarget = false;

            var note = LedgerV2.Mono(chip, textX, -22f, textW,
                TurfOrders.Label(crew.Order), 10.8f, LedgerV2.Muted, 10f);
            note.raycastTarget = false;

            LedgerV2.StreetMark(chip, textX, -44f, LedgerV2.Red, 8f);
            var men = LedgerV2.Figure(chip, textX + 13f, -36f, textW - 13f,
                crew.MenStanding + " men", 13.2f, LedgerV2.Ink,
                TextAlignmentOptions.MidlineLeft);
            men.raycastTarget = false;
        }

        // ------------------------------------------------------------- personal file

        /// <summary>
        /// The crew whose file is up: the one the plate has been asked to inspect, and
        /// otherwise whoever is picked on the street. One question with one answer -
        /// picking a name up on the plan and finding nobody picked when the wheel comes
        /// back down was the very thing TurfMapHud.Changed exists to prevent.
        /// </summary>
        TurfCrew Subject()
        {
            if (_hud == null)
                return null;

            var inspected = _hud.InspectedCrew;
            if (inspected != null)
                return inspected;

            var picked = _crews != null ? _crews.Selected : null;
            if (picked == null)
                return null;

            var units = _hud.Units;
            for (var i = 0; i < units.Count; i++)
                if (units[i].Unit == picked)
                    return units[i];
            return null;
        }

        void PaintFileIfMoved()
        {
            // The plate is stood up after the crews are dealt, so it cannot be handed
            // over at Init - it is asked for until it answers.
            if (_hud == null)
            {
                _hud = FindAnyObjectByType<TurfMapHud>();
                if (_hud == null)
                    return;
            }

            var crew = Subject();
            var id = crew != null ? crew.Id : int.MinValue;

            if (_hud.CrewFileVersion != _paintedCrewFile || id != _paintedCrewId ||
                RosterStamp() != _paintedChipRoster)
            {
                _paintedCrewFile = _hud.CrewFileVersion;
                _paintedCrewId = id;
                _paintedChipRoster = RosterStamp();
                PaintChips(crew);
                PaintFile(crew);
                return;
            }

            // Men fall and orders land while nothing about the PICK has moved. Written in
            // place: a rebuild of the whole dossier every time somebody starts walking
            // would be a panel that flickers for nothing.
            if (crew == null)
                return;

            if (_menText != null && crew.MenStanding != _paintedStanding)
            {
                _paintedStanding = crew.MenStanding;
                _menText.text = MenLine(crew);
            }
            if (_orderText != null && crew.Order != _paintedOrder)
            {
                _paintedOrder = crew.Order;
                _orderText.text = TurfOrders.Label(crew.Order);
            }
        }

        static string MenLine(TurfCrew crew) =>
            crew.MenStanding + " of " + (crew.HoodsOnBooks + 1) + " men";

        void PaintFile(TurfCrew crew)
        {
            if (_fileRoot != null)
                Destroy(_fileRoot.gameObject);
            _fileRoot = null;
            _night.ForgetDead();
            _menText = _orderText = null;
            _paintedStanding = -1;
            _paintedOrder = (TurfOrder)(-1);

            if (crew == null)
                return;

            _paintedStanding = crew.MenStanding;
            _paintedOrder = crew.Order;

            var card = LedgerV2.Card("Personal file", _canvas.transform, 0f, -FileTop,
                FileWide, 10f);
            _fileRoot = card;

            var y = Head(card, "Personal file");

            // ---- his picture, his name, and how many of him are on their feet
            var plate = LedgerV2.PortraitPlate(card, FilePad, y, PlateWide, PlateTall,
                InitialsOf(crew.Name));
            Picture(plate, crew);

            var textX = FilePad + PlateWide + 10f;
            var textW = FileWide - textX - FilePad;
            LedgerV2.Name(card, textX, y - 2f, textW, crew.Name);
            if (crew.Rank.Length > 0)
                LedgerV2.Mono(card, textX, y - 24f, textW, crew.Rank, 9f, LedgerV2.Red, 10f);

            // A solid mark: these are men standing on a street, not a number on a page.
            LedgerV2.StreetMark(card, textX, y - 44f, LedgerV2.Red, 9f);
            _menText = LedgerV2.Figure(card, textX + 15f, y - 45f, textW - 15f,
                MenLine(crew), 13f, LedgerV2.Ink, TextAlignmentOptions.MidlineLeft);

            _orderText = LedgerV2.Mono(card, textX, y - 66f, textW,
                TurfOrders.Label(crew.Order), 9.5f, LedgerV2.Muted, 8f);

            y -= PlateTall + 10f;

            // ---- his three trades, on the ledger's own ten-mark half-step meter
            Rule(card, FilePad, y, FileWide - FilePad * 2f, LedgerV2.Hair);
            y -= 10f;
            y = Trade(card, y, "Awareness", crew.Awareness);
            y = Trade(card, y, "Organization", crew.Organization);
            y = Trade(card, y, "Combat", crew.Combat);

            // ---- what he carries, and what he rides in
            y -= 4f;
            Rule(card, FilePad, y, FileWide - FilePad * 2f, LedgerV2.Hair);
            y -= 10f;
            LedgerV2.Mono(card, FilePad, y, FileWide - FilePad * 2f, "Carrying", 9.5f,
                LedgerV2.Label, 10f);
            y -= 18f;
            y = Band(card, y, crew.Gun);
            y = Band(card, y, crew.Ride);

            // ---- the orders, every one of them through the plate's one gateway
            y -= 4f;
            y = LedgerV2.Section(card, FilePad, y, FileWide - FilePad * 2f, "Orders");
            y = Orders(card, y, crew);

            y -= FilePad;

            // The card was laid at a placeholder height; only now does the file know how
            // tall the man made it. Its shadows and its face stretch with it.
            card.sizeDelta = new Vector2(FileWide, -y);
            _night.Register(card);
        }

        /// <summary>
        /// The six orders, three to a row, exactly as the crew column offered them and
        /// through the same call: TurfMapHud.Order, the one gateway. FLEE is the red key
        /// because it is the one that gives ground.
        /// </summary>
        float Orders(RectTransform card, float y, TurfCrew crew)
        {
            const float gutter = 4f, tall = 28f;
            var inner = FileWide - FilePad * 2f;

            if (!crew.Mine)
            {
                LedgerV2.Button(card, "Watched, not spoken to", FilePad, y, inner, tall,
                    null, LedgerV2.Key.Outline, 8.5f);
                return y - tall;
            }

            var cell = (inner - gutter * 2f) / 3f;
            Order(card, FilePad, y, cell, tall, crew, "Guard", TurfOrder.Holding,
                LedgerV2.Key.Outline);
            Order(card, FilePad + cell + gutter, y, cell, tall, crew, "Patrol",
                TurfOrder.Walking, LedgerV2.Key.Outline);
            Order(card, FilePad + (cell + gutter) * 2f, y, cell, tall, crew, "Tail",
                TurfOrder.Moving, LedgerV2.Key.Outline);
            y -= tall + gutter;
            Order(card, FilePad, y, cell, tall, crew, "Home", TurfOrder.ToTheOutfit,
                LedgerV2.Key.Ghost);
            Order(card, FilePad + cell + gutter, y, cell, tall, crew, "Car",
                TurfOrder.InTheCar, LedgerV2.Key.Ghost);
            Order(card, FilePad + (cell + gutter) * 2f, y, cell, tall, crew, "Flee",
                TurfOrder.PullingBack, LedgerV2.Key.Red);
            return y - tall;
        }

        void Order(RectTransform card, float x, float y, float w, float h, TurfCrew crew,
            string label, TurfOrder order, LedgerV2.Key key) =>
            LedgerV2.Button(card, label, x, y, w, h,
                () => _hud.Order(order, crew.Plan, null), key, 9f);

        /// <summary>One trade: the word, and the ten-mark meter the personnel file and
        /// the plate both print it on. Never a star rating of its own.</summary>
        static float Trade(RectTransform card, float y, string label, int halfSteps)
        {
            var pipsX = FileWide - FilePad - LedgerV2.PipsWidth(Meter, 5f, 7f);
            LedgerV2.Mono(card, FilePad, y, pipsX - FilePad - 6f, label, 10f,
                LedgerV2.Label, 8f);
            LedgerV2.Pips(card, pipsX, y - 9f, Meter, Mathf.Clamp(halfSteps, 0, Meter),
                LedgerV2.Red, 5f, 11f, 7f);
            return y - 20f;
        }

        /// <summary>
        /// The file's dark head band. Not LedgerV2.CardHead: that one reserves 200 units
        /// on the right for a stamp, which on a card this narrow leaves the title fifty
        /// and prints it as "PERSONA.". Answers the y below the band.
        /// </summary>
        float Head(RectTransform card, string label)
        {
            const float tall = 30f;
            var band = NewRect("Head", card);
            PlaceTopLeft(band, 0f, 0f, FileWide, tall);
            Fill(band, LedgerV2.Head);

            var title = Caps(band, 12f, -(tall - 12f) * 0.5f, FileWide - 46f, label, 10f,
                LedgerV2.HeadInk, 13f);
            title.font = LedgerStyle.MonoBold;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.raycastTarget = false;

            // The design's close is a grey glyph in the band, not a red key: red is
            // the ink of a verb that undoes something, and shutting a file undoes
            // nothing.
            var shut = LedgerV2.Button(card, "x", FileWide - 30f, 4f, 22f, 22f,
                () => _hud.ClearInspection(), LedgerV2.Key.Ghost, 18f);
            shut.color = LedgerV2.HeadDim;
            return -tall - 8f;
        }

        /// <summary>One of the two bands under CARRYING: a sunk strip with the piece, or
        /// the car, set in the typewriter face. Answers the y below it.</summary>
        static float Band(RectTransform card, float y, string text)
        {
            var band = NewRect("Band", card);
            PlaceTopLeft(band, FilePad, y, FileWide - FilePad * 2f, 20f);
            Fill(band, LedgerV2.PanelBand);
            Frame(band, 1f, LedgerV2.Hair);
            var line = Line(band, LedgerStyle.Type, 12f, LedgerV2.Ink, 6f, -1f,
                FileWide - FilePad * 2f - 12f, 18f, text);
            line.overflowMode = TextOverflowModes.Ellipsis;
            return y - 24f;
        }

        /// <summary>
        /// His picture. The crew bar already films every lieutenant on one shared,
        /// rotating camera; this borrows that feed rather than standing up a second one,
        /// and falls back to the portrait studio's still print for a man the bar is not
        /// filming - a rival, or one off the bottom of the row.
        /// </summary>
        static void Picture(RawImage plate, TurfCrew crew)
        {
            if (crew.Unit != null)
                plate.gameObject.AddComponent<LieutenantFeed>().Unit = crew.Unit;

            if (crew.Lieutenant != null)
                PortraitStudio.Request(PersonnelAlmanac.MemberModel(crew.Lieutenant),
                    PortraitStudio.Framing.Bust, plate);
        }

        /// <summary>Connects the file's plate to CrewBar's one shared feed. It owns no
        /// camera and no RenderTexture of its own: moving the picture onto a second
        /// panel must not double the cost of filming the same lieutenant. Where there is
        /// no feed it leaves the plate exactly as it found it, so the studio's still
        /// print stands.</summary>
        sealed class LieutenantFeed : MonoBehaviour
        {
            public DemoCrews.Unit Unit;
            RawImage _image;

            void Awake() => _image = GetComponent<RawImage>();

            void LateUpdate()
            {
                if (CrewBar.Instance == null ||
                    !CrewBar.Instance.TryGetFeed(Unit, out var feed))
                    return;

                if (_image.texture != feed)
                    _image.texture = feed;
                _image.enabled = true;
            }
        }

        static string InitialsOf(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return "";
            var parts = fullName.Split(' ');
            var first = parts[0].Length > 0 ? parts[0].Substring(0, 1) : "";
            var last = parts.Length > 1 && parts[parts.Length - 1].Length > 0
                ? parts[parts.Length - 1].Substring(0, 1)
                : "";
            return (first + last).ToUpperInvariant();
        }

        // -------------------------------------------------------------------- wire

        readonly struct WireLine
        {
            public readonly string Source, Stamp, Body, Tag, Figure;
            public readonly Color Ink;

            public WireLine(string source, string stamp, string body, string tag,
                string figure, Color ink)
            {
                Source = source;
                Stamp = stamp;
                Body = body;
                Tag = tag;
                Figure = figure;
                Ink = ink;
            }
        }

        /// <summary>The height the tag row adds to a slip that carries one.</summary>
        const float TagTall = 18f;

        /// <summary>
        /// The design's tag row: what KIND of thing came in, set in the slip's own stock
        /// on a block of its ink, and beside it what it cost. It reads before the
        /// sentence does, which is the point of it - a boss scanning the wire sees MAN
        /// DOWN and a heat figure without reading a word of the report.
        /// </summary>
        static void Tag(RectTransform slip, WireLine line)
        {
            var wide = LedgerV2.ButtonWidth(line.Tag, 10.8f, 18f, 5f);
            var block = NewRect("Tag", slip);
            PlaceTopLeft(block, 12f, -22f, wide, 15f);
            Fill(block, line.Ink);

            var word = Caps(block, 0f, -1f, wide, line.Tag, 10.8f, LedgerStyle.Slip, 18f,
                TextAlignmentOptions.Center);
            word.font = LedgerStyle.MonoBold;

            if (line.Figure.Length == 0)
                return;

            var figure = Line(slip, LedgerStyle.MonoBold, 13.2f, line.Ink,
                12f + wide + 6f, -22f, WireWide - wide - 36f, 15f, line.Figure);
            figure.characterSpacing = 4f;
        }

        /// <summary>
        /// The ink a slip's edge is ruled in - the design's rule that a wire is read by
        /// colour before it is read by word. Every one of these is a pen the book
        /// already writes in: the red for blood, the blue ballpoint for a man of ours
        /// who is no longer ours, amber for money being asked for, green for a promotion,
        /// and plain for the rest.
        /// </summary>
        static Color InkOf(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Froze:
                case IncidentKind.Fled:
                case IncidentKind.Escalated:
                case IncidentKind.DiedOnTheDetail:
                case IncidentKind.StoppedIt:
                    return LedgerStyle.RedPen;
                case IncidentKind.TookRivalMoney:
                case IncidentKind.Defected:
                case IncidentKind.BearsWatching:
                case IncidentKind.CaughtSkimming:
                    return LedgerStyle.Ballpoint;
                case IncidentKind.DemandedARaise:
                    return LedgerStyle.PenAmber;
                case IncidentKind.Promoted:
                    return LedgerStyle.GreenOk;
                default:
                    return LedgerStyle.TelexPlain;
            }
        }

        void PaintWire(OutfitDirector outfit)
        {
            if (_wireRoot != null)
                Destroy(_wireRoot.gameObject);
            _night.ForgetDead();

            var root = NewRect("The wire", _canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(WireWide, WireHeadTall);
            _wireRoot = root;

            var head = NewRect("Head", root);
            PlaceTopLeft(head, 0f, 0f, WireWide, WireHeadTall);
            Fill(head, LedgerV2.Head);
            RowButton(head, ClickSurface(head), () => _wireOpen = !_wireOpen);

            var title = Caps(head, 9f, -(WireHeadTall - LineBox(13.9f)) * 0.5f, 180f,
                "The wire", 13.9f, LedgerV2.HeadCream, 18f);
            title.font = LedgerStyle.Condensed;
            title.raycastTarget = false;

            var lines = Lines(outfit);
            // The count is of REAL traffic. The placeholder line is the machine saying
            // it is working, not a message, and "1 OF 0" is a readout that contradicts
            // itself on the very first night of a campaign.
            var filed = outfit != null ? outfit.Incidents.Count : 0;
            var count = Caps(head, WireWide - 96f,
                -(WireHeadTall - LineBox(12f)) * 0.5f, 62f,
                filed == 0 ? "Quiet"
                    : (_wireOpen ? Mathf.Min(lines.Count, filed) : 0) + " of " + filed,
                12f, LedgerV2.HeadDim, 10f, TextAlignmentOptions.MidlineRight);
            count.font = LedgerStyle.Mono;
            count.raycastTarget = false;

            var caret = Line(head, LedgerStyle.MonoBold, 13.2f, LedgerStyle.RailGold,
                WireWide - 26f, -(WireHeadTall - 16f) * 0.5f, 18f, 16f,
                _wireOpen ? "-" : "+",
                TextAlignmentOptions.MidlineRight);
            caret.raycastTarget = false;

            if (!_wireOpen)
                return;

            var y = -(WireHeadTall + 5f);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var tall = line.Tag.Length > 0 ? SlipTall + TagTall : SlipTall;
                var slip = Slip(root, 0f, y, WireWide, tall,
                    line.Source, line.Stamp, line.Body, line.Ink, line.Tag.Length > 0);
                if (line.Tag.Length > 0)
                    Tag(slip, line);
                // Newest at full strength and the ones behind it stepping back: the wire
                // is read from the top, and the tail is only there for context.
                slip.gameObject.AddComponent<CanvasGroup>().alpha = 1f - i * 0.18f;
                y -= tall + WireGap;
            }
            root.sizeDelta = new Vector2(WireWide, -y);
            _night.Register(root);
        }

        /// <summary>
        /// What is on the wire: the incidents the campaign has already written, newest
        /// first. Nothing here composes a sentence - IncidentText set every one of these
        /// the day it happened, and the paper, the ledger and this strip print the same
        /// words.
        /// </summary>
        List<WireLine> Lines(OutfitDirector outfit)
        {
            _lines.Clear();
            if (outfit == null)
                return _lines;

            var book = outfit.Incidents;
            for (var i = book.Count - 1; i >= 0 && _lines.Count < WireLines; i--)
            {
                var incident = book[i];
                _lines.Add(new WireLine(
                    incident.Where.Length > 0
                        ? "WIRE - " + incident.Where.ToUpperInvariant()
                        : "WIRE",
                    "DAY " + incident.Day,
                    incident.Line,
                    LedgerText.IncidentLabel(incident.Kind),
                    // The figure the design puts beside the tag is whatever this one
                    // cost. For an incident that is the police attention it drew, and
                    // an incident that drew none says nothing rather than nothing-shaped.
                    incident.Heat > 0 ? "+" + incident.Heat + " HEAT" : "",
                    InkOf(incident.Kind)));
            }

            // A wire with nothing on it reads as a machine that has failed, not a quiet
            // night - the ledger's own rule for the same strip.
            if (_lines.Count == 0)
                _lines.Add(new WireLine("WIRE", "DAY " + outfit.Campaign.Day,
                    "Nothing on the wire. Nobody of ours has done a thing he was not told to.",
                    "", "", LedgerStyle.TelexPlain));
            return _lines;
        }

        // --------------------------------------------------------------- ledger key

        void PaintLedgerKey()
        {
            var root = NewRect("Ledger key", _canvas.transform);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(KeyWide, KeyTall);
            _keyRoot = root;

            Fill(root, LedgerStyle.RailGold);
            RowButton(root, ClickSurface(root), OpenLedger);

            // Both words are centred on the key's own height rather than hung from its
            // top: a line box is taller than the type in it and taller again for larger
            // type, so two labels dropped from the same y sit on two different lines.
            var label = Caps(root, 10f, -(KeyTall - LineBox(15f)) * 0.5f,
                KeyWide - 56f, "Open the ledger", 15f, LedgerV2.Ink, 16f);
            label.font = LedgerStyle.Condensed;
            label.raycastTarget = false;

            // The key it is actually bound to, in the boss's own book - and it is P, not
            // TAB: the almanac has answered to P since the book existed.
            var hint = Caps(root, KeyWide - 42f, -(KeyTall - LineBox(12f)) * 0.5f, 32f,
                "P", 12f,
                new Color(LedgerV2.Ink.r, LedgerV2.Ink.g, LedgerV2.Ink.b, 0.62f), 10f,
                TextAlignmentOptions.MidlineRight);
            hint.font = LedgerStyle.Mono;
            hint.raycastTarget = false;
        }

        static void OpenLedger()
        {
            var book = PersonnelAlmanac.Instance;
            if (book)
                book.Open();
        }
    }

    /// <summary>
    /// The two clicks a crew chip answers. Unity's Button knows only the left one, and
    /// the right click here is not a menu - it is the roster's old "put the camera on
    /// him", which worked at every zoom level and had to keep working when the chip
    /// replaced the roster.
    /// </summary>
    sealed class ChipClicks : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Pick, Ride;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                Ride?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Left)
                Pick?.Invoke();
        }
    }
}
