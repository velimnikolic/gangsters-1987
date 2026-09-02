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
        /// <summary>Whether the PERSONAL FILE is put up under the chips at all.</summary>
        const bool FileShown = false;
        const float PlateWide = 124f, PlateTall = 84f;
        const int Meter = 10;   // the ledger's half-step scale, and the map's

        // ---- the wire ----
        const float WireWide = 326f;
        const float WireHeadTall = 26f;
        /// <summary>One line per slip (2026-09-01, the user's word): the sentence
        /// on the left, the day on the right, the kind's ink down the edge, and
        /// nothing else. The source, the tag and the figure came off the street -
        /// the four-line telex was the ledger's, and out here it stood taller than
        /// the chips it sat beside.</summary>
        const float SlipTall = 20f;
        const float WireGap = 4f;
        /// <summary>How many slips the strip carries. The design's own default, inside
        /// the two-to-six range it lets a boss set.</summary>
        const int WireLines = 4;

        /// <summary>
        /// How many of the strip's slots the doorstep news is guaranteed - HALF, and
        /// never all of them.
        ///
        /// The racket is the thing the player is doing, so the door slips lead. But the
        /// strip used to guarantee them every slot, and four dispatches is one afternoon
        /// of asking: past that no incident - a man losing his temper, skimming, walking
        /// out - ever reached the street again. Half each, and the books cannot starve
        /// one another.
        /// </summary>
        const int DoorLinesKept = WireLines / 2;

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
        int _paintedDoorNews = -1;

        TurfMapHud _hud;
        RectTransform _chipRoot;
        int _paintedChipRoster = -1;

        /// <summary>One painted chip's live parts: the line that says what the crew is
        /// doing, the mark and headcount under it, and the red reason a refused order
        /// puts in their place.</summary>
        sealed class ChipFace
        {
            public TurfCrew Crew;
            public TextMeshProUGUI Note, Refusal;
            public RectTransform Count;
            public string Shown;
            public bool Refused;
        }

        readonly List<ChipFace> _chips = new List<ChipFace>();

        // What the file is painted against. The plate bumps CrewFileVersion whenever the
        // pick moves; the two live figures under it move without it.
        int _paintedCrewFile = -1;
        int _paintedCrewId = int.MinValue;
        int _paintedStanding = -1;
        TextMeshProUGUI _menText;

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
            // Keep the screen-edge HUD fully opaque over the 3D city.
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
            // region - so it is laid at full strength and the fifth the panels are
            // seeing through is a fifth of an already darkened street. Riding inside the
            // group would put the wash at four fifths as well, and the panels would be
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

            // These two pieces belong to the street, not to the open book. Hide them
            // explicitly as well as suspending the HUD canvas, so they cannot remain
            // over the ledger for a frame if canvas update order changes.
            SetLedgerFurnitureVisible(!PersonnelAlmanac.IsOpen);

            var want = !ModalUp;
            if (_canvas.enabled != want)
                _canvas.enabled = want;
            if (!want)
                return;

            _night.Relight();
            PaintFileIfMoved();
            RefreshChips();

            var outfit = OutfitDirector.Instance;
            var incidents = outfit != null ? outfit.Incidents.Count : 0;
            var day = outfit != null ? outfit.Campaign.Day : -1;
            // AND THE RACKET. The strip used to repaint only when the incident count or
            // the day moved, so door news was filed into the feed and the wire went on
            // showing whatever it had been painted with - a strip that prints the first
            // few things that ever happened and then stops for the rest of the game.
            var racket = TerritoryRuntime.Instance?.Racket;
            var doorNews = racket != null ? racket.Version : 0;

            if (incidents == _paintedIncidents && day == _paintedCampaignDay &&
                doorNews == _paintedDoorNews && _wireOpen == _paintedWireOpen)
                return;

            _paintedIncidents = incidents;
            _paintedCampaignDay = day;
            _paintedDoorNews = doorNews;
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
                // The order still decides which MARK the chip carries - solid for men
                // afield, hatched for a crew walking home - so an order landing has to
                // redraw the row. What he is DOING is written in place (RefreshChips).
                stamp = stamp * 31 + (int)crew.Order;
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
            _chips.Clear();
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

            var face = new ChipFace { Crew = crew };
            _chips.Add(face);

            var textX = 3f + ChipPlate + 9f;
            var textW = ChipWide - textX - 12f;
            // Three lines in sixty-two units. TMP's line box is far taller than the
            // type in it, so the boxes are allowed to overlap and it is the CENTRES
            // that are spaced - stacking the boxes end to end would need seventy-two
            // and push the count off the bottom of the bar.
            var name = LedgerV2.Name(chip, textX, -2f, textW, crew.Name, 15f);
            name.raycastTarget = false;

            // What he is DOING, not what he was last told: the card that floated over a
            // selected lieutenant said it in a sentence and was withdrawn from the
            // street (2026-09-02, the user's word), so the chip's line carries it in the
            // two or three words it has room for. A crew with nobody on the street left
            // to watch falls back to the standing order, which is all such a crew has.
            // Upper case, as every measured label on this HUD is: the design sets the
            // chip's status line in the mono face's caps.
            face.Note = LedgerV2.Mono(chip, textX, -22f, textW, StatusWord(crew), 10.8f,
                LedgerV2.Muted, 10f);
            face.Note.raycastTarget = false;
            face.Shown = face.Note.text;

            // The mark and the headcount ride together, because a refused order takes
            // the pair of them off the chip for its couple of seconds.
            var count = NewRect("Count", chip);
            PlaceTopLeft(count, 0f, 0f, ChipWide, BarTall);
            face.Count = count;

            // The design's two marks, and they say different things: a SOLID red square
            // for a lieutenant whose men are out on the street, and the hatched blue
            // one - the ink the book writes what is only on PAPER in - for a man who is
            // in the house. A crew walking home or with nobody left standing is not
            // afield, and printing the street's own mark beside it would say it was.
            if (Afield(crew))
                LedgerV2.StreetMark(count, textX, -44f, LedgerV2.Red, 8f);
            else
                LedgerV2.PaperMark(count, textX, -44f, LedgerV2.PaperBlue, 8f);
            var men = LedgerV2.Figure(count, textX + 13f, -36f, textW - 13f,
                crew.MenStanding + " men", 13.2f, LedgerV2.Ink,
                TextAlignmentOptions.MidlineLeft);
            men.raycastTarget = false;

            // And why an order was refused, in the words the system that refused used -
            // red, wrapped over the status and the count, for as long as CrewOverlay
            // holds it. It is the only thing the withdrawn card said that no other
            // panel says, and a boss who clicks and sees nothing happen is owed it.
            face.Refusal = Paragraph(chip, LedgerStyle.Mono, 8.5f, LedgerV2.Red, textX,
                -18f, textW, BarTall - 22f, "", lineSpacing: 1f);
            face.Refusal.overflowMode = TextOverflowModes.Ellipsis;
            face.Refusal.raycastTarget = false;
            face.Refusal.gameObject.SetActive(false);
        }

        /// <summary>The chip's status line: what the crew is doing on the street, or the
        /// standing order for a crew the street has no unit for.</summary>
        static string StatusWord(TurfCrew crew) =>
            CrewStatus.Short(crew.Unit) ?? TurfOrders.Label(crew.Order).ToUpperInvariant();

        /// <summary>
        /// The chips say two things that move without the row being rebuilt: what the
        /// crew is doing, which changes every time a man starts walking, and the reason
        /// an order was refused, which is up for a couple of seconds. Both are written
        /// into the text that is already standing - a row of chips destroyed and rebuilt
        /// at that rate would flicker for nothing.
        /// </summary>
        void RefreshChips()
        {
            for (var i = 0; i < _chips.Count; i++)
            {
                var face = _chips[i];
                if (face.Crew == null || face.Note == null)
                    continue;

                var refusal = CrewOverlay.RefusalFor(face.Crew.Unit);
                var refused = refusal != null;
                if (refused != face.Refused)
                {
                    face.Refused = refused;
                    face.Note.gameObject.SetActive(!refused);
                    if (face.Count != null)
                        face.Count.gameObject.SetActive(!refused);
                    face.Refusal.gameObject.SetActive(refused);
                }

                var line = refused ? refusal : StatusWord(face.Crew);
                if (line == face.Shown)
                    continue;
                face.Shown = line;
                if (refused) face.Refusal.text = line;
                else face.Note.text = line;
            }
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
        }

        /// <summary>Are this lieutenant's men out on the street, or is he in the house?
        /// The design carries the answer as a MARK rather than a word - solid for the
        /// street, hatched for the paper - and the two states it draws are exactly
        /// these: somebody standing out there, and nobody.</summary>
        static bool Afield(TurfCrew crew) =>
            crew.MenStanding > 0 && crew.Order != TurfOrder.ToTheOutfit;

        static string MenLine(TurfCrew crew) =>
            crew.MenStanding + " of " + (crew.HoodsOnBooks + 1) + " men";

        void PaintFile(TurfCrew crew)
        {
            if (_fileRoot != null)
                Destroy(_fileRoot.gameObject);
            _fileRoot = null;
            _night.ForgetDead();
            _menText = null;
            _paintedStanding = -1;

            if (crew == null)
                return;

            // The file is withdrawn from the street (2026-09-01, the user's word): the
            // chip at the top names the man and his order, and the dossier under it
            // stayed shut. The picker still runs so the chips and the plate agree on
            // who is picked; only the paper is not put up.
            if (!FileShown)
                return;

            _paintedStanding = crew.MenStanding;

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

            // The name, and under it his headcount. NOTHING else: the design puts the
            // rank and what he is doing on the CHIP at the top of the screen, and a
            // dossier that prints them a second time is the CREWS AFIELD column all
            // over again - the same fact in two places, drifting apart the moment
            // either is touched. A solid mark, because these are men standing on a
            // street and not a number on a page.
            LedgerV2.StreetMark(card, textX, y - 28f, LedgerV2.Red, 9f);
            _menText = LedgerV2.Figure(card, textX + 15f, y - 29f, textW - 15f,
                MenLine(crew), 15.6f, LedgerV2.Ink, TextAlignmentOptions.MidlineLeft);

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
            // The design's caption is 10px on a 0.16em tape - a hair larger and a good
            // deal wider than the kit's default label, which is what makes it read as a
            // caption over two boxes rather than a fourth stat row.
            LedgerV2.Mono(card, FilePad, y, FileWide - FilePad * 2f, "CARRYING", 12f,
                LedgerV2.Label, 16f);
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

        /// <summary>What the six order keys are lettered at. The design overrides the
        /// kit's own key here - [data-key-cell] sets 11px against the DS key's 8.7 -
        /// because these six are the panel's verbs and not a row of filters, and at the
        /// kit's size they read as small print on a card of stat rows.</summary>
        const float OrderKeyType = 13.2f;

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
                () => _hud.Order(order, crew.Plan, null), key, OrderKeyType);

        /// <summary>One trade: the word, and the ten-mark meter the personnel file and
        /// the plate both print it on. Never a star rating of its own.</summary>
        static float Trade(RectTransform card, float y, string label, int halfSteps)
        {
            var pipsX = FileWide - FilePad - LedgerV2.PipsWidth(Meter, 5f, 7f);
            LedgerV2.Mono(card, FilePad, y, pipsX - FilePad - 6f,
                label.ToUpperInvariant(), 13.2f, LedgerV2.Label, 10f);
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
            var shut = LedgerV2.Button(card, "\u00d7", FileWide - 26f, -2f, 22f, 22f,
                () => _hud.ClearInspection(), LedgerV2.Key.Ghost, 18f);
            shut.color = LedgerV2.HeadDim;
            // Grey until the pointer is on it, and then the rail's red: shutting a file
            // undoes nothing, so the key is not red until it is about to be pressed.
            HoverTint.On((RectTransform)shut.transform.parent, shut, LedgerV2.HeadDim,
                LedgerStyle.RailRed);
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
            // The design sets these two in the typewriter face at 12px. Lekton prints
            // larger than the face this book's sizes were written against - the measured
            // 1.082 - so 12px on the page is 11.1 asked for here.
            var line = Line(band, LedgerStyle.Type, 11.1f, LedgerV2.Ink, 6f, -1f,
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

        // The slip itself, the ink it is ruled in and the words on it are WireBook's -
        // the ledger's rail prints the same run out of the same book, and two strips
        // that composed their own sentences would be two accounts of one night.

        /// <summary>A slip cut to one line: stock, the kind's ink at full strength
        /// down the left edge, the sentence in the mono face cut off with an ellipsis
        /// where it runs out of room, and the day stamp on the right.</summary>
        static RectTransform OneLineSlip(RectTransform parent, float y, WireLine line)
        {
            const float StampWide = 44f;
            var rect = NewRect("Slip", parent);
            PlaceTopLeft(rect, 0f, y, WireWide, SlipTall);
            Stock(rect, LedgerStyle.Slip, LedgerStyle.SlipLow);

            var edge = NewRect("Edge", rect);
            PlaceTopLeft(edge, 0f, 0f, 3f, SlipTall);
            Fill(edge, line.Ink);

            var copy = Line(rect, LedgerStyle.Mono, 11f, LedgerStyle.InkSoft,
                9f, 0f, WireWide - 9f - StampWide - 12f, SlipTall, line.Body);
            copy.enableWordWrapping = false;
            copy.overflowMode = TextOverflowModes.Ellipsis;
            copy.raycastTarget = false;

            var stamp = Caps(rect, WireWide - StampWide - 6f, -(SlipTall - 15f) * 0.5f,
                StampWide, line.Stamp, 9f, LedgerStyle.InkLabel, 2f,
                TextAlignmentOptions.MidlineRight);
            stamp.raycastTarget = false;
            return rect;
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
            var headFace = Fill(head, LedgerV2.Head);
            RowButton(head, ClickSurface(head), () => _wireOpen = !_wireOpen);
            // The design lifts the band a shade under the pointer, so a strip that can
            // be shut says so before it is pressed.
            HoverTint.On(head, headFace, LedgerV2.Head, LedgerV2.Rgb2(0x241c17));

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
                _wireOpen ? "\u2014" : "+",
                TextAlignmentOptions.MidlineRight);
            caret.raycastTarget = false;

            if (!_wireOpen)
                return;

            var y = -(WireHeadTall + 5f);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var tall = SlipTall;
                var slip = OneLineSlip(root, y, line);

                // Newest at full strength and the ones behind it stepping back: the wire
                // is read from the top, and the tail is only there for context. It steps
                // back by AGE and not by importance, so the pointer brings any of them
                // back to full - a boss reading the third slip down is reading it, not
                // glancing past it.
                var group = slip.gameObject.AddComponent<CanvasGroup>();
                var fade = slip.gameObject.AddComponent<WireSlip>();
                fade.Rest = 1f - i * 0.18f;
                fade.Group = group;
                // The newest slip arrives: it comes in from eight units above and fades
                // up over the design's 260ms, so a message landing on a busy screen is
                // seen to land instead of merely being there next time the eye passes.
                fade.Arrive = i == 0;
                ClickSurface(slip);

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
            var doors = TerritoryRuntime.Instance?.Racket?.Dispatches;

            // TWO books, one strip. The incidents are what OUR MEN did that nobody
            // ordered; the dispatches are what happened AT A DOOR - the answer an owner
            // gave, the front that went in. A boss on the map used to be told the first
            // and never the second, so the whole racket played out in silence unless he
            // had the ledger open on the right page.
            //
            // The two are NOT sorted against each other. They are counted on different
            // clocks - the campaign's day and the city clock's - and comparing them let
            // the incidents take every slot, which is how door news came to be filed and
            // never printed. Instead the strip GUARANTEES the racket a share: the last
            // few doors first, then the incidents, then more doors if the strip has room.
            var incident = book.Count - 1;
            var door = doors != null ? doors.Count - 1 : -1;

            for (var kept = 0; kept < DoorLinesKept && door >= 0 && _lines.Count < WireLines;
                 kept++, door--)
                _lines.Add(WireBook.Of(doors[door]));

            while (incident >= 0 && _lines.Count < WireLines)
            {
                _lines.Add(WireBook.Of(book[incident]));
                incident--;
            }

            while (door >= 0 && _lines.Count < WireLines)
            {
                _lines.Add(WireBook.Of(doors[door]));
                door--;
            }

            // A wire with nothing on it reads as a machine that has failed, not a quiet
            // night - the ledger's own rule for the same strip.
            if (_lines.Count == 0)
                _lines.Add(new WireLine("WIRE", "DAY " + outfit.Campaign.Day,
                    "Nothing on the wire. Nobody of ours has done a thing he was not told to.",
                    "", "", LedgerStyle.TelexPlain, outfit.Campaign.Day));
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

            var face = Fill(root, LedgerStyle.RailGold);
            RowButton(root, ClickSurface(root), OpenLedger);
            HoverTint.On(root, face, LedgerStyle.RailGold, LedgerV2.Rgb2(0xf7d788));

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

        void OpenLedger()
        {
            var book = PersonnelAlmanac.Instance;
            if (book)
            {
                // The click happens before the book's next visual update. Put the key
                // and the wire away immediately instead of leaving either over it for
                // the remainder of this frame.
                SetLedgerFurnitureVisible(false);
                book.Open();
            }
        }

        void SetLedgerFurnitureVisible(bool visible)
        {
            if (_keyRoot && _keyRoot.gameObject.activeSelf != visible)
                _keyRoot.gameObject.SetActive(visible);
            if (_wireRoot && _wireRoot.gameObject.activeSelf != visible)
                _wireRoot.gameObject.SetActive(visible);
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

    /// <summary>
    /// A surface that lifts a shade under the pointer, in the design's own second
    /// colour rather than a brightness multiplier.
    ///
    /// Unity's own Button tint MULTIPLIES the graphic it is given, which cannot reach a
    /// named hex: the ledger key's hover is #f7d788 against a gold of #d4a73e, and no
    /// single factor takes one to the other. And the colour it goes back to is asked of
    /// <see cref="HudNight.Cross"/> rather than remembered, because a band remembered at
    /// build is a DAY colour that would be repainted onto a night panel the first time
    /// the pointer left it.
    /// </summary>
    sealed class HoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Graphic _face;
        Color _rest, _over;

        public static void On(RectTransform rect, Graphic face, Color rest, Color over)
        {
            var tint = rect.gameObject.AddComponent<HoverTint>();
            tint._face = face;
            tint._rest = rest;
            tint._over = over;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_face != null)
                _face.color = _over;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_face != null)
                _face.color = HudNight.Cross(_rest);
        }
    }

    /// <summary>
    /// How one slip on the wire behaves: it arrives, it steps back as the strip fills
    /// behind it, and it comes back to full under the pointer.
    ///
    /// The stepping back is by AGE - the newest slip is at full and each one behind it
    /// is a fifth fainter - which is a way of SORTING, not of saying that an older
    /// message matters less. So the pointer undoes it: a boss reading the third slip
    /// down is reading it, and it prints as solid as the first.
    ///
    /// Unscaled time throughout. The clock has a HOLD rung and runs at four times as
    /// well, and a message landing must take the same quarter second to land at every
    /// one of them.
    /// </summary>
    sealed class WireSlip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>The design's own three figures: how long a slip takes to arrive,
        /// how far above its place it starts, and how long the fade under the pointer
        /// takes.</summary>
        const float ArriveSeconds = 0.26f, ArriveRise = 8f, HoverSeconds = 0.14f;

        public CanvasGroup Group;

        /// <summary>What this slip prints at when the pointer is elsewhere.</summary>
        public float Rest = 1f;

        /// <summary>Whether this one is the message that just came in.</summary>
        public bool Arrive;

        RectTransform _rect;
        Vector2 _home;
        float _arrival, _shown;
        bool _hover;

        void Start()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            _arrival = Arrive ? 0f : 1f;
            _shown = Arrive ? 0f : Rest;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData) => _hover = true;
        public void OnPointerExit(PointerEventData eventData) => _hover = false;

        void Update()
        {
            if (Group == null)
                return;

            var step = Time.unscaledDeltaTime;
            if (_arrival < 1f)
                _arrival = Mathf.Min(1f, _arrival + step / ArriveSeconds);

            var want = _hover ? 1f : Rest;
            _shown = Mathf.MoveTowards(_shown, want, step / HoverSeconds);
            Apply();
        }

        /// <summary>Where the slip stands and how solid it is, this frame. The arrival
        /// eases OUT - fast off the mark and settling in - which is the curve the design
        /// names, and it multiplies the resting strength rather than replacing it, so a
        /// slip that lands already stepped back lands at the strength it will keep.
        /// </summary>
        void Apply()
        {
            var eased = 1f - (1f - _arrival) * (1f - _arrival);
            Group.alpha = _shown * eased;
            if (_rect != null)
                _rect.anchoredPosition =
                    new Vector2(_home.x, _home.y + ArriveRise * (1f - eased));
        }
    }
}
