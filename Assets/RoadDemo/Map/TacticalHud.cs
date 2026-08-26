using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The terminal the raster sits in: the masthead and its four readouts, the map
    /// well, the side rail of four panels, the footer strips, the turf toggle, the
    /// context menu and the CRT glass.
    ///
    /// None of it touches a pixel of the map. That is the design sheet's own division -
    /// the HUD is a layer OVER the upscaled canvas, not something baked into 320x200 -
    /// and it is what keeps the chrome readable on a big screen while the map itself
    /// stays a low-resolution picture. The scanlines and the vignette are the same kind
    /// of thing: glass in front of the tube, laid over whatever is behind it, and
    /// switchable without re-rasterising anything.
    ///
    /// It is laid out by hand rather than by layout groups, because every measurement in
    /// the handoff is a pixel figure against a 1460-wide sheet: the canvas is scaled to
    /// exactly that width, so every number below is the number the sheet gives rather
    /// than a conversion of one. A layout group would have been fewer lines and none of
    /// those numbers would have survived it.
    ///
    /// Laying rects out by hand is also the easiest thing here to get quietly wrong, so
    /// there are exactly three ways to place one - <see cref="Stretch"/>,
    /// <see cref="Pin"/> and <see cref="Fill"/> - and nothing in this file touches
    /// anchors, pivots or offsets outside them.
    ///
    /// A plain class and not a component: the map owns the canvas because the map owns
    /// the modes, and this builds inside whatever rect it is handed.
    /// </summary>
    public sealed class TacticalHud
    {
        /// <summary>The sheet's own page width. The canvas is scaled so one unit here is
        /// one pixel of the handoff.</summary>
        public const float PageWidth = 1460f;

        public const float PageHeight = 1013f;

        const float Gutter = 13f;
        const float RailWidth = 300f;
        const float RailMin = 252f;
        const float PanelPad = 11f;
        const float PanelGap = 0f;
        const float HeadingHeight = 16f;
        const float HeaderHeight = 92f;
        const float FooterHeight = 26f;
        const float StripHeight = 22f;
        const float WellPad = 6f;

        const float StatWidth = 108f;
        const float StatHeight = 44f;
        const float StatGap = 6f;

        const float GangRowHeight = 16f;
        const float CrewRowHeight = 34f;
        const float MenuRowHeight = 30f;
        const float MenuTitleHeight = 22f;
        const float LogRowHeight = 15f;

        /// <summary>Under this width the rail has nowhere to stand and the map takes the
        /// whole panel - which is the docked mode beside the open ledger.</summary>
        const float RailNeeds = 760f;

        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);
        static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>What every overlay on this terminal stands on. The map is the screen
        /// now, so a panel with nothing behind it is a panel you cannot read.</summary>
        static readonly Color Scrim = new Color(6f / 255f, 10f / 255f, 8f / 255f, 0.94f);

        // --------------------------------------------------------------- the pieces

        RectTransform _root;
        RectTransform _header;
        RectTransform _body;
        RectTransform _footer;
        RectTransform _strip;

        RectTransform _well;        // the bordered box the map lives in
        RectTransform _surface;     // the map itself, at 320:200
        RectTransform _labels;      // the on-map lettering
        RectTransform _glass;
        RawImage _scanlineImage;
        RectTransform _menu;

        TMP_Text _cityName;
        TMP_Text _crewValue;
        TMP_Text _manpowerValue;
        TMP_Text _selectedValue;
        TMP_Text _heldValue;
        TMP_Text _clockValue;
        TMP_Text _countStrip;

        RectTransform _rail;
        RectTransform[] _panels;
        float[] _panelHeights;
        RectTransform _gangRows;
        RectTransform _rosterRows;
        RectTransform _actionRow;
        TMP_Text _inspectHead;
        TMP_Text _inspectBody;
        Image _blip;
        readonly List<TMP_Text> _logLines = new List<TMP_Text>();

        Image _turfButton;
        TMP_Text _turfLabel;
        TMP_Text _menuTitle;
        RectTransform _menuItems;

        readonly List<RectTransform> _gangPool = new List<RectTransform>();
        readonly List<RectTransform> _rosterPool = new List<RectTransform>();
        readonly List<RectTransform> _actionPool = new List<RectTransform>();
        readonly List<RectTransform> _menuPool = new List<RectTransform>();
        readonly List<LabelChip> _labelPool = new List<LabelChip>();

        Sprite _frame;
        Texture2D _scanlines;
        Texture2D _vignette;

        Vector2 _laidFor;
        bool _laidChrome = true;
        bool _laidOnce;

        // ------------------------------------------------------------------ wiring

        public System.Action OnToggleTurf;
        public System.Action OnSelectAll;
        public System.Action<int> OnPickCrew;
        public System.Action<int> OnFocusCrew;
        public System.Action<int> OnAction;
        public System.Action<int> OnMenuItem;

        public RectTransform Surface => _surface;
        public bool MenuOpen => _menu != null && _menu.gameObject.activeSelf;

        // ------------------------------------------------------------------- build

        public void Build(RectTransform parent, MapRaster raster, MapSurface.IReader reader)
        {
            _root = New("Terminal", parent);
            Fill(_root);

            _header = New("Header", _root);
            BuildHeader(_header);

            // Sibling order IS draw order: the map first and everything else after it,
            // so every panel prints over the sheet.
            _body = New("Body", _root);
            BuildWell(_body, raster, reader);

            _rail = New("Rail", _root);
            BuildRail(_rail);

            _strip = New("Strip", _root);
            BuildStrip(_strip);

            _footer = New("Footer", _root);
            BuildFooter(_footer);

            _header.SetAsLastSibling();
            BuildMenu(_surface);
        }

        // ------------------------------------------------------------------ header

        void BuildHeader(RectTransform header)
        {
            var scrim = New("Scrim", header);
            Pin(scrim, TopLeft, Vector2.zero, new Vector2(560f, HeaderHeight));
            var wash = scrim.gameObject.AddComponent<Image>();
            wash.color = Scrim;
            wash.raycastTarget = false;

            var pad = New("Pad", header);
            Fill(pad, 14f, 14f, 8f, 6f);

            var kicker = Label(pad, "Kicker",
                "STREET COMMAND TERMINAL // TACTICAL SHEET 04", 12f, MapPalette.Muted, 0.20f);
            Stretch(kicker.rectTransform, 0f, 0f, 4f, 13f);

            _cityName = Label(pad, "City", "THE CITY", 30f, MapPalette.Strong, 0.06f, true);
            Stretch(_cityName.rectTransform, 0f, 0f, 21f, 34f);

            var subtitle = Label(pad, "Subtitle",
                "TURF CONTROL MAP - 1:1 FOOTPRINT SURVEY - 1987", 11f, MapPalette.Heading, 0.16f);
            Stretch(subtitle.rectTransform, 0f, 0f, 59f, 13f);

            _crewValue = Stat(header, "YOUR CREW", 0);
            _manpowerValue = Stat(header, "MANPOWER", 1);
            _selectedValue = Stat(header, "SELECTED", 2);
            _heldValue = Stat(header, "BLOCKS HELD", 3);
            _clockValue = Stat(header, "CLOCK", 4);

            var mine = MapPalette.Gang(LivingCity.Gangs.GangCatalog.PlayerGangId);
            _crewValue.color = mine;
            _manpowerValue.color = mine;
            _heldValue.color = mine;
            _clockValue.color = MapPalette.Heading;

            // The rule under the masthead.
            var rule = New("Rule", header);
            rule.anchorMin = BottomLeft;
            rule.anchorMax = new Vector2(1f, 0f);
            rule.pivot = new Vector2(0.5f, 0f);
            rule.offsetMin = Vector2.zero;
            rule.offsetMax = new Vector2(0f, 2f);
            Paint(rule, MapPalette.Rule);
        }

        TMP_Text Stat(RectTransform header, string caption, int slot)
        {
            var box = New("Stat " + caption, header);
            Pin(box, TopRight, new Vector2(-(4 - slot) * (StatWidth + StatGap), -4f),
                new Vector2(StatWidth, StatHeight));
            var face = box.gameObject.AddComponent<Image>();
            face.color = Scrim;
            face.raycastTarget = false;
            Outline(box, MapPalette.Rule);

            var captionText = Label(box, "Caption", caption, 10f, MapPalette.Muted, 0.14f);
            Stretch(captionText.rectTransform, 9f, 9f, 6f, 11f);

            var value = Label(box, "Value", "-", 17f, MapPalette.Strong, 0.06f, true);
            Stretch(value.rectTransform, 9f, 9f, 20f, 18f);
            return value;
        }

        // -------------------------------------------------------------------- well

        void BuildWell(RectTransform body, MapRaster raster, MapSurface.IReader reader)
        {
            _well = New("Map Well", body);
            var face = _well.gameObject.AddComponent<Image>();
            face.color = MapPalette.Well;
            face.raycastTarget = true;

            // The map takes the whole screen and everything else floats on it, so the
            // sheet is scaled to COVER rather than to fit: square pixels kept, the
            // overflow cut off by this mask rather than framed in bars.
            _well.gameObject.AddComponent<RectMask2D>();

            _surface = New("Surface", _well);
            var screen = _surface.gameObject.AddComponent<RawImage>();
            screen.texture = raster.Texture;
            screen.raycastTarget = true;

            // TURNED OVER ON THE WAY OUT, and this is not cosmetic. The raster is
            // written the way the design sheet draws one and the way a screen scans -
            // row nought at the TOP, north - but Texture2D.SetPixels32 fills from the
            // BOTTOM left, so a buffer handed over as it stands comes out upside down.
            // It cost an afternoon: the city looked perfectly plausible mirrored, the
            // giveaway was that W walked the camera down the map and every click landed
            // on the building reflected about the middle row.
            screen.uvRect = new Rect(0f, 1f, 1f, -1f);

            var surface = _surface.gameObject.AddComponent<MapSurface>();
            surface.Reader = reader;

            BuildGlass(_surface);

            _labels = New("Labels", _surface);
            Fill(_labels);
            var pass = _labels.gameObject.AddComponent<CanvasGroup>();
            pass.blocksRaycasts = false;
            pass.interactable = false;

            BuildTurfButton(_surface);
        }

        void BuildTurfButton(RectTransform surface)
        {
            // Bottom left of the sheet: the readouts have the top right now.
            var button = New("Turf", surface);
            Pin(button, BottomLeft, new Vector2(8f, 8f), new Vector2(150f, 22f));

            _turfButton = button.gameObject.AddComponent<Image>();
            _turfButton.color = MapPalette.Button;
            Outline(button, MapPalette.PlayerAccent);

            _turfLabel = Label(button, "Label", "TURF OVERLAY: ON", 11f,
                MapPalette.PlayerAccent, 0.14f);
            Fill(_turfLabel.rectTransform);
            _turfLabel.alignment = TextAlignmentOptions.Center;

            var press = button.gameObject.AddComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.onClick.AddListener(() => OnToggleTurf?.Invoke());
        }

        /// <summary>The glass: scanlines over the tube and a vignette in the corners.
        /// Both are textures generated once, both are over the map and under the
        /// lettering, and neither is baked into the raster.</summary>
        void BuildGlass(RectTransform surface)
        {
            _glass = New("Glass", surface);
            Fill(_glass);
            var pass = _glass.gameObject.AddComponent<CanvasGroup>();
            pass.blocksRaycasts = false;
            pass.interactable = false;

            if (_scanlines == null)
            {
                _scanlines = new Texture2D(1, 3, TextureFormat.RGBA32, false)
                {
                    name = "Scanlines",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                };
                _scanlines.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.32f));
                _scanlines.SetPixel(0, 1, Color.clear);
                _scanlines.SetPixel(0, 2, Color.clear);
                _scanlines.Apply();
            }

            var lines = New("Scanlines", _glass);
            Fill(lines);
            _scanlineImage = lines.gameObject.AddComponent<RawImage>();
            _scanlineImage.texture = _scanlines;
            _scanlineImage.raycastTarget = false;

            if (_vignette == null)
                _vignette = MakeVignette();

            var corners = New("Vignette", _glass);
            Fill(corners);
            var cornerImage = corners.gameObject.AddComponent<RawImage>();
            cornerImage.texture = _vignette;
            cornerImage.raycastTarget = false;
        }

        /// <summary>The sheet's inset corner shadow, drawn as a texture because uGUI has
        /// no such thing.</summary>
        static Texture2D MakeVignette()
        {
            const int N = 64;
            var texture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "Vignette",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (var y = 0; y < N; y++)
            {
                for (var x = 0; x < N; x++)
                {
                    var u = Mathf.Abs(x / (N - 1f) * 2f - 1f);
                    var v = Mathf.Abs(y / (N - 1f) * 2f - 1f);
                    var edge = Mathf.Max(u, v);
                    var dark = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, edge));
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, dark * 0.6f));
                }
            }
            texture.Apply();
            return texture;
        }

        // -------------------------------------------------------------------- rail

        void BuildRail(RectTransform rail)
        {
            var scrim = New("Scrim", rail);
            Fill(scrim);
            var wash = scrim.gameObject.AddComponent<Image>();
            wash.color = Scrim;
            wash.raycastTarget = true;

            var gangs = Panel(_rail, "Gangs", "GANGS & TURF", MapPalette.Heading,
                out _gangRows);
            // Twenty-one families and the ground nobody holds is a longer list than the
            // design sheet's five, so the panel clips rather than printing over the
            // roster below it. What survives the clip is the caller's business: the map
            // ranks the list before it hands it over.
            _gangRows.gameObject.AddComponent<RectMask2D>();

            var roster = Panel(_rail, "Roster", "CREW ROSTER", MapPalette.PlayerAccent,
                out _rosterRows);
            BuildAllButton(roster);

            var inspector = Panel(_rail, "Inspector", "NOTHING SELECTED", MapPalette.Heading,
                out var inspectorBody);
            _inspectHead = inspector.Find("Heading").GetComponent<TMP_Text>();

            _inspectBody = Label(inspectorBody, "Body",
                "Click a building, a crew, or drag a box over your men.",
                11f, MapPalette.Body, 0.05f);
            Stretch(_inspectBody.rectTransform, 0f, 0f, 0f, 96f);
            _inspectBody.alignment = TextAlignmentOptions.TopLeft;
            _inspectBody.textWrappingMode = TextWrappingModes.Normal;
            _inspectBody.lineSpacing = 30f;

            _actionRow = New("Actions", inspectorBody);
            Stretch(_actionRow, 0f, 0f, 100f, 44f);

            var log = Panel(_rail, "Log", "RADIO LOG", MapPalette.Heading, out var logBody);
            for (var i = 0; i < 4; i++)
            {
                var line = Label(logBody, "Line " + i, string.Empty, 10f,
                    MapPalette.UnclaimedChrome, 0.06f);
                Stretch(line.rectTransform, 0f, 0f, i * LogRowHeight, 12f);
                _logLines.Add(line);
            }

            _blip = Paint(New("Blip", logBody), MapPalette.PlayerAccent);
            Pin(_blip.rectTransform, BottomLeft, new Vector2(0f, 3f), new Vector2(6f, 6f));

            var live = Label(logBody, "Online", "COMMAND ONLINE", 10f,
                MapPalette.PlayerAccent, 0.12f);
            Pin(live.rectTransform, BottomLeft, new Vector2(11f, 0f), new Vector2(160f, 12f));

            _panels = new[] { gangs, roster, inspector, log };
            // The gang list takes whatever the other three leave, so a city of
            // twenty-one families still fits beside a map.
            _panelHeights = new[] { 0f, 250f, 330f, 112f };
        }

        void BuildAllButton(RectTransform roster)
        {
            var all = New("All", roster);
            Pin(all, TopRight, new Vector2(-PanelPad, -PanelPad + 1f), new Vector2(34f, 16f));
            var face = all.gameObject.AddComponent<Image>();
            face.color = Clear;
            Outline(all, MapPalette.Rule);

            var label = Label(all, "Label", "ALL", 10f, MapPalette.Muted, 0.10f);
            Fill(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;

            var button = all.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnSelectAll?.Invoke());
        }

        RectTransform Panel(RectTransform rail, string name, string heading, Color accent,
            out RectTransform body)
        {
            var panel = New(name, rail);
            var face = panel.gameObject.AddComponent<Image>();
            // Not transparent any more: these stand ON the map. The sheet's own on-map
            // chips are near-black at .86; a panel carrying a column of 8 px type over a
            // lit block wants more than a chip does, so it takes .94.
            face.color = Scrim;
            face.raycastTarget = true;
            Outline(panel, MapPalette.Rule);

            var title = Label(panel, "Heading", heading, 12f, accent, 0.20f, true);
            Stretch(title.rectTransform, PanelPad, PanelPad, PanelPad, HeadingHeight);

            body = New("Body", panel);
            Fill(body, PanelPad, PanelPad, PanelPad + HeadingHeight + 6f, PanelPad);
            return panel;
        }

        // ------------------------------------------------------------------ footer

        void BuildFooter(RectTransform footer)
        {
            var scrim = New("Scrim", footer);
            Fill(scrim);
            var wash = scrim.gameObject.AddComponent<Image>();
            wash.color = Scrim;
            wash.raycastTarget = false;

            var keys = Label(footer, "Keys", "F1 KEY   F2 TURF   F3 ORDERS   ESC CLEAR",
                11f, MapPalette.Dim, 0.14f);
            keys.rectTransform.anchorMin = TopLeft;
            keys.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            keys.rectTransform.pivot = new Vector2(0.5f, 1f);
            keys.rectTransform.offsetMin = new Vector2(0f, -20f);
            keys.rectTransform.offsetMax = new Vector2(0f, -8f);

            var feed = Label(footer, "Feed", "MINIMAP FEED READY - SAME RASTER SOURCE",
                11f, MapPalette.Dim, 0.14f);
            feed.alignment = TextAlignmentOptions.Right;
            feed.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            feed.rectTransform.anchorMax = TopRight;
            feed.rectTransform.pivot = new Vector2(0.5f, 1f);
            feed.rectTransform.offsetMin = new Vector2(0f, -20f);
            feed.rectTransform.offsetMax = new Vector2(0f, -8f);

            var rule = New("Rule", footer);
            Stretch(rule, 0f, 0f, 0f, 2f);
            Paint(rule, MapPalette.Rule);
        }

        void BuildStrip(RectTransform strip)
        {
            var scrim = New("Scrim", strip);
            Fill(scrim);
            var wash = scrim.gameObject.AddComponent<Image>();
            wash.color = Scrim;
            wash.raycastTarget = false;

            var hints = new[]
            {
                "DRAG = SELECT CREW", "RIGHT CLICK = ORDERS", "CLICK BUILDING = INSPECT",
            };
            for (var i = 0; i < hints.Length; i++)
            {
                var text = Label(strip, "Hint " + i, hints[i], 11f, MapPalette.Muted, 0.12f);
                var rect = text.rectTransform;
                rect.anchorMin = new Vector2(i / 4f, 0f);
                rect.anchorMax = new Vector2((i + 1) / 4f, 1f);
                rect.pivot = Middle;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                text.alignment = TextAlignmentOptions.Left;
            }

            _countStrip = Label(strip, "Count", "0 BUILDINGS", 11f, MapPalette.Muted, 0.12f);
            var count = _countStrip.rectTransform;
            count.anchorMin = new Vector2(0.75f, 0f);
            count.anchorMax = Vector2.one;
            count.pivot = Middle;
            count.offsetMin = Vector2.zero;
            count.offsetMax = Vector2.zero;
            _countStrip.alignment = TextAlignmentOptions.Right;
        }

        // -------------------------------------------------------------------- menu

        void BuildMenu(RectTransform surface)
        {
            _menu = New("Orders", surface);
            _menu.anchorMin = TopLeft;
            _menu.anchorMax = TopLeft;
            _menu.pivot = TopLeft;

            // The sheet's hard offset shadow: no blur, three pixels down and right.
            var shadow = New("Shadow", _menu);
            Fill(shadow);
            shadow.offsetMin = new Vector2(3f, -3f);
            shadow.offsetMax = new Vector2(3f, -3f);
            Paint(shadow, new Color(0f, 0f, 0f, 0.6f));

            var face = _menu.gameObject.AddComponent<Image>();
            face.color = MapPalette.MenuBack;

            _menuTitle = Label(_menu, "Title", "NO SELECTION - MAP", 11f,
                MapPalette.Heading, 0.14f, true);
            Stretch(_menuTitle.rectTransform, 9f, 9f, 4f, 12f);

            var rule = New("Rule", _menu);
            Stretch(rule, 0f, 0f, MenuTitleHeight - 1f, 1f);
            Paint(rule, MapPalette.Rule);

            _menuItems = New("Items", _menu);
            Fill(_menuItems, 0f, 0f, MenuTitleHeight, 0f);

            Outline(_menu, MapPalette.PlayerAccent);
            _menu.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>
        /// Re-lays the terminal for the rect it now has. The map keeps 320:200 come what
        /// may - it is a picture of a fixed shape, and letterboxing it is the only honest
        /// thing to do with the space left over.
        /// </summary>
        public void Layout(Vector2 size, bool chrome)
        {
            if (size.x < 1f || size.y < 1f)
                return;
            if (_laidOnce && size == _laidFor && chrome == _laidChrome)
                return;
            _laidFor = size;
            _laidChrome = chrome;
            _laidOnce = true;

            var railOn = chrome && size.x >= RailNeeds;
            _header.gameObject.SetActive(chrome);
            _footer.gameObject.SetActive(chrome);
            _strip.gameObject.SetActive(chrome);
            _rail.gameObject.SetActive(railOn);

            // The sheet takes the whole panel and is scaled to COVER it: at 16:9 that
            // cuts about ten rows off the top and bottom of a 16:10 raster, which is
            // the price of the map being the screen rather than a picture on it.
            Fill(_body);
            Fill(_well);

            var scale = Mathf.Max(size.x / MapRaster.W, size.y / MapRaster.H);
            var mapWidth = MapRaster.W * scale;
            var mapHeight = MapRaster.H * scale;
            Pin(_surface, Middle, Vector2.zero, new Vector2(mapWidth, mapHeight));

            // Scanlines are tiled in SCREEN pixels: glass in front of the tube, and it
            // does not scale with the picture.
            if (_scanlineImage != null)
                _scanlineImage.uvRect = new Rect(0f, 0f, 1f, mapHeight / 3f);

            if (!chrome)
                return;

            // Hard into the corners. There is no page margin here: the map IS the
            // screen, so an overlay floated in off the edge only wastes map and leaves a
            // strip of city too narrow to read between the panel and the frame.
            Pin(_header, TopLeft, Vector2.zero, new Vector2(size.x, HeaderHeight));

            _footer.anchorMin = BottomLeft;
            _footer.anchorMax = new Vector2(1f, 0f);
            _footer.pivot = new Vector2(0.5f, 0f);
            _footer.offsetMin = Vector2.zero;
            _footer.offsetMax = new Vector2(0f, FooterHeight);

            Pin(_strip, BottomLeft, new Vector2(0f, FooterHeight),
                new Vector2(size.x, StripHeight));

            if (!railOn)
                return;

            // The rail hangs under the readouts on the right and stops above the strip.
            var railWidth = Mathf.Max(RailMin, Mathf.Min(RailWidth, size.x * 0.24f));
            var railTop = HeaderHeight;
            var railHeight = Mathf.Max(200f,
                size.y - railTop - FooterHeight - StripHeight);
            Pin(_rail, TopRight, new Vector2(0f, -railTop),
                new Vector2(railWidth, railHeight));

            var taken = 0f;
            for (var i = 1; i < _panels.Length; i++)
                taken += _panelHeights[i] + PanelGap;
            var gangHeight = Mathf.Max(110f, railHeight - taken);

            var y = 0f;
            for (var i = 0; i < _panels.Length; i++)
            {
                var height = i == 0 ? gangHeight : _panelHeights[i];
                Stretch(_panels[i], 0f, 0f, y, height);
                y += height + PanelGap;
            }
        }

        // ------------------------------------------------------------------- values

        public void SetCity(string name)
        {
            if (_cityName != null)
                _cityName.text = string.IsNullOrEmpty(name) ? "THE CITY" : name.ToUpperInvariant();
        }

        /// <summary>
        /// The readouts. Every one of these is a figure somebody counted this half
        /// second - none is a constant - and SELECTED says both what was picked and how
        /// many men that came to, because three markers and eleven men are different
        /// facts and the player is planning with the second one.
        /// </summary>
        public void SetStats(string crew, int manpower, int crews, int men,
            int heldPercent, string clock)
        {
            if (_crewValue != null)
                _crewValue.text = string.IsNullOrEmpty(crew) ? "-" : crew.ToUpperInvariant();
            if (_manpowerValue != null)
                _manpowerValue.text = manpower + " MEN";
            if (_selectedValue != null)
                _selectedValue.text = crews == 0 ? "NONE" : crews + " / " + men + " MEN";
            if (_heldValue != null) _heldValue.text = heldPercent + "%";
            if (_clockValue != null) _clockValue.text = clock;
        }

        public void SetCount(int buildings, float metresPerPixel)
        {
            if (_countStrip != null)
                _countStrip.text = buildings + " BUILDINGS - " +
                                   metresPerPixel.ToString("0.0") + " M/PX";
        }

        public void SetTurf(bool on)
        {
            if (_turfButton == null)
                return;
            _turfLabel.text = on ? "TURF OVERLAY: ON" : "TURF OVERLAY: OFF";
            _turfLabel.color = on ? MapPalette.PlayerAccent : MapPalette.UnclaimedChrome;
            Recolour(_turfButton.rectTransform,
                on ? MapPalette.PlayerAccent : MapPalette.ToggleOff);
        }

        public void Blip(float time)
        {
            if (_blip == null)
                return;
            var colour = MapPalette.PlayerAccent;
            colour.a = time % 1.1f < 0.55f ? 1f : 0.2f;
            _blip.color = colour;
        }

        // ------------------------------------------------------------- the gang list

        public struct GangRow
        {
            public Color Colour;
            public string Name;
            public int People;
            public int Percent;
        }

        public void SetGangs(List<GangRow> rows)
        {
            Grow(_gangPool, _gangRows, rows.Count, GangRowFactory);
            for (var i = 0; i < _gangPool.Count; i++)
            {
                var row = _gangPool[i];
                row.gameObject.SetActive(i < rows.Count);
                if (i >= rows.Count)
                    continue;
                Stretch(row, 0f, 0f, i * GangRowHeight, 15f);
                row.Find("Swatch").GetComponent<Image>().color = rows[i].Colour;
                row.Find("Name").GetComponent<TMP_Text>().text =
                    (rows[i].Name ?? "-").ToUpperInvariant();
                row.Find("People").GetComponent<TMP_Text>().text = rows[i].People + " PPL";
                row.Find("Percent").GetComponent<TMP_Text>().text = rows[i].Percent + "%";
            }
        }

        RectTransform GangRowFactory(RectTransform parent, int index)
        {
            var row = New("Gang " + index, parent);
            Stretch(row, 0f, 0f, 0f, 15f);

            var swatch = Paint(New("Swatch", row), Color.white);
            Pin(swatch.rectTransform, MiddleLeft, Vector2.zero, new Vector2(10f, 10f));
            Outline(swatch.rectTransform, Color.black);

            var name = Label(row, "Name", string.Empty, 11f, MapPalette.MenuText, 0.06f);
            Fill(name.rectTransform, 18f, 96f, 0f, 0f);
            name.alignment = TextAlignmentOptions.Left;

            var people = Label(row, "People", string.Empty, 10f, MapPalette.UnclaimedChrome, 0.06f);
            Pin(people.rectTransform, MiddleRight, new Vector2(-40f, 0f), new Vector2(54f, 12f));
            people.alignment = TextAlignmentOptions.Right;

            var percent = Label(row, "Percent", string.Empty, 11f, MapPalette.Strong, 0.06f);
            Pin(percent.rectTransform, MiddleRight, Vector2.zero, new Vector2(38f, 12f));
            percent.alignment = TextAlignmentOptions.Right;
            return row;
        }

        // ---------------------------------------------------------------- the roster

        public struct CrewRow
        {
            public int CrewId;

            /// <summary>The lieutenant's own name - a crew IS its leader here.</summary>
            public string Name;

            public string Rank;
            public string Alias;
            public int Men;
            public string Weapon;
            public string Order;

            /// <summary>The prefab the leader was cast from. It keys his portrait
            /// through PortraitStudio's cache - the same print the inspector shows
            /// larger, so a face learned in the list is the face on the card.</summary>
            public GameObject Mug;

            /// <summary>Nought to one, the whole crew's health against its whole
            /// strength - one bar for the men, not one per man.</summary>
            public float Condition;

            public bool Selected;
        }

        public void SetRoster(List<CrewRow> rows)
        {
            Grow(_rosterPool, _rosterRows, rows.Count, RosterRowFactory);
            for (var i = 0; i < _rosterPool.Count; i++)
            {
                var row = _rosterPool[i];
                row.gameObject.SetActive(i < rows.Count);
                if (i >= rows.Count)
                    continue;

                var data = rows[i];
                Stretch(row, 0f, 0f, i * CrewRowHeight, 32f);
                row.Find("Name").GetComponent<TMP_Text>().text =
                    (data.Name ?? "-").ToUpperInvariant();
                row.Find("Rank").GetComponent<TMP_Text>().text = data.Rank ?? string.Empty;
                row.Find("Under").GetComponent<TMP_Text>().text =
                    (data.Alias ?? "-") + "  x" + data.Men + " MEN  " + (data.Weapon ?? "-");
                row.Find("Order").GetComponent<TMP_Text>().text = data.Order;

                Face((RawImage)row.Find("Mug").GetComponent<RawImage>(), data.Mug);

                var fill = (RectTransform)row.Find("Bar/Fill");
                fill.sizeDelta = new Vector2(Mathf.Round(30f * Mathf.Clamp01(data.Condition)), 4f);
                fill.GetComponent<Image>().color = data.Condition > 0.6f ? MapPalette.HpGood
                    : data.Condition > 0.3f ? MapPalette.HpFair : MapPalette.HpPoor;

                row.GetComponent<Image>().color = data.Selected ? MapPalette.RowBack : Clear;
                Recolour(row, data.Selected ? MapPalette.PlayerAccent : MapPalette.RowIdle);
                row.Find("Name").GetComponent<TMP_Text>().color =
                    data.Selected ? MapPalette.RowText : MapPalette.Body;

                var id = data.CrewId;
                var button = row.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnPickCrew?.Invoke(id));

                // The right button on a name is not an order - it is "show me where he
                // is". A Button only ever answers the left one, so the row carries a
                // second, smaller handler for the other.
                row.GetComponent<RowClick>().Clicked = which =>
                {
                    if (which == PointerEventData.InputButton.Right)
                        OnFocusCrew?.Invoke(id);
                };
            }
        }

        RectTransform RosterRowFactory(RectTransform parent, int index)
        {
            var row = New("Crew " + index, parent);
            Stretch(row, 0f, 0f, 0f, 20f);
            var face = row.gameObject.AddComponent<Image>();
            face.color = Clear;
            Outline(row, MapPalette.RowIdle);
            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            row.gameObject.AddComponent<RowClick>();

            // His face, 22x26, at the head of his own row.
            var mug = New("Mug", row);
            Pin(mug, MiddleLeft, new Vector2(4f, 0f), new Vector2(22f, 26f));
            var shot = mug.gameObject.AddComponent<RawImage>();
            shot.raycastTarget = false;
            shot.enabled = false;
            shot.uvRect = Bust(22f, 26f);

            var name = Label(row, "Name", string.Empty, 11f, MapPalette.Body, 0.06f);
            Stretch(name.rectTransform, 30f, 96f, 3f, 12f);
            name.alignment = TextAlignmentOptions.Left;

            var rank = Label(row, "Rank", string.Empty, 9f, MapPalette.Heading, 0.10f);
            Pin(rank.rectTransform, TopRight, new Vector2(-4f, -3f), new Vector2(92f, 12f));
            rank.alignment = TextAlignmentOptions.Right;

            var under = Label(row, "Under", string.Empty, 9f, MapPalette.UnclaimedChrome, 0.04f);
            Stretch(under.rectTransform, 30f, 4f, 15f, 11f);
            under.alignment = TextAlignmentOptions.Left;

            var order = Label(row, "Order", string.Empty, 9f, MapPalette.Muted, 0.06f);
            Pin(order.rectTransform, new Vector2(1f, 0f), new Vector2(-36f, 3f),
                new Vector2(90f, 11f));
            order.alignment = TextAlignmentOptions.Right;

            var bar = Paint(New("Bar", row), MapPalette.Rule);
            Pin(bar.rectTransform, new Vector2(1f, 0f), new Vector2(-4f, 5f),
                new Vector2(30f, 4f));

            var fill = Paint(New("Fill", bar.rectTransform), MapPalette.HpGood);
            Pin(fill.rectTransform, MiddleLeft, Vector2.zero, new Vector2(30f, 4f));
            return row;
        }

        // -------------------------------------------------------------- the inspector

        public void SetInspector(string head, string body, List<string> actions)
        {
            if (_inspectHead != null) _inspectHead.text = head ?? string.Empty;
            if (_inspectBody != null)
            {
                _inspectBody.gameObject.SetActive(true);
                _inspectBody.text = body;
            }
            if (_crewCard != null)
                _crewCard.gameObject.SetActive(false);

            LayoutActions(actions, 100f);
        }

        /// <summary>The row of verbs under whatever the card is showing. Laid at a given
        /// height because a crew card is taller than a building's paragraph.</summary>
        void LayoutActions(List<string> actions, float top)
        {
            Stretch(_actionRow, 0f, 0f, top, 44f);
            var count = actions?.Count ?? 0;
            Grow(_actionPool, _actionRow, count, ActionFactory);

            var x = 0f;
            var y = 0f;
            var room = Mathf.Max(80f, _actionRow.rect.width);
            for (var i = 0; i < _actionPool.Count; i++)
            {
                var chip = _actionPool[i];
                chip.gameObject.SetActive(i < count);
                if (i >= count)
                    continue;

                var text = chip.Find("Label").GetComponent<TMP_Text>();
                text.text = actions[i];
                var width = Mathf.Max(44f, text.GetPreferredValues(actions[i]).x + 14f);
                if (x + width > room && x > 0f)
                {
                    x = 0f;
                    y += 20f;
                }
                Pin(chip, TopLeft, new Vector2(x, -y), new Vector2(width, 16f));
                x += width + 5f;

                var index = i;
                var button = chip.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnAction?.Invoke(index));
            }
        }

        RectTransform ActionFactory(RectTransform parent, int index)
        {
            var chip = New("Action " + index, parent);
            Pin(chip, TopLeft, Vector2.zero, new Vector2(60f, 16f));
            var face = chip.gameObject.AddComponent<Image>();
            face.color = Clear;
            Outline(chip, MapPalette.Rule);
            var button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;

            var label = Label(chip, "Label", string.Empty, 10f, MapPalette.MenuText, 0.10f);
            Fill(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            return chip;
        }

        /// <summary>A row that answers the buttons a <see cref="Button"/> will not.
        /// uGUI's own component takes the left click and drops every other, which is
        /// almost always right and is exactly wrong on a list of names.</summary>
        sealed class RowClick : MonoBehaviour, IPointerClickHandler
        {
            public System.Action<PointerEventData.InputButton> Clicked;

            public void OnPointerClick(PointerEventData eventData) =>
                Clicked?.Invoke(eventData.button);
        }

        // ----------------------------------------------------------- the crew card

        RectTransform _crewCard;
        RawImage _crewFace;
        TMP_Text _crewCaption;
        TMP_Text _crewFacts;
        RectTransform _crewBook;
        readonly List<RectTransform> _bookPool = new List<RectTransform>();

        /// <summary>
        /// The crew card: his face and his name on the left, the crew's figures on the
        /// right, and under both the men on the book - one row each, with what he is
        /// carrying and how he is holding up.
        ///
        /// The portrait is the same print as the roster row's, at 66x80 instead of
        /// 22x26, which is the whole reason the leader's prefab is carried around as a
        /// key: one face, learned in the list, recognised on the card.
        /// </summary>
        public void SetCrewCard(MapCrew crew, string outfit, string order, List<string> actions)
        {
            BuildCrewCard();

            _inspectBody.gameObject.SetActive(false);
            _crewCard.gameObject.SetActive(true);
            if (_inspectHead != null)
                _inspectHead.text = (crew.Name ?? "-").ToUpperInvariant();

            Face(_crewFace, crew.Mug);
            _crewCaption.text = "LT. " + (crew.Surname ?? "-").ToUpperInvariant();

            _facts.Clear();
            Fact("OUTFIT", outfit);
            Fact("CREW", crew.Alias);
            Fact("STRENGTH", "x" + crew.Strength + " MEN");
            Fact("CONDITION", Mathf.RoundToInt(crew.Condition * 100f) + "%");
            Fact("ORDER", order);
            Fact("RIDE", crew.Ride);
            Fact("HEAT", crew.Heat > 0 ? crew.Heat + " WANTED" : "CLEAN");
            Fact("LOYALTY", crew.Loyalty + "%");
            // WAGE and not TAKE: this project has no per-crew income, and printing a
            // cost under a heading that says "take" would be a lie with a real number
            // attached. See MapCrews.
            Fact("WEEKLY WAGE", "$" + crew.Wage);
            Fact("POSITION", Mathf.RoundToInt(crew.Position.x) + ", " +
                             Mathf.RoundToInt(crew.Position.z));
            _crewFacts.text = _facts.ToString();

            var colour = MapPalette.Gang(crew.Gang);
            Grow(_bookPool, _crewBook, crew.Men.Count, BookRowFactory);
            for (var i = 0; i < _bookPool.Count; i++)
            {
                var row = _bookPool[i];
                var on = i < crew.Men.Count;
                if (row.gameObject.activeSelf != on)
                    row.gameObject.SetActive(on);
                if (!on)
                    continue;

                var man = crew.Men[i];
                Stretch(row, 0f, 0f, i * 13f, 12f);
                row.Find("Dot").GetComponent<Image>().color = colour;
                row.Find("Name").GetComponent<TMP_Text>().text =
                    (man.Name ?? "-").ToUpperInvariant();
                row.Find("Role").GetComponent<TMP_Text>().text = man.Role ?? "-";
                row.Find("Arm").GetComponent<TMP_Text>().text = man.Weapon ?? "-";
                var fill = (RectTransform)row.Find("Bar/Fill");
                fill.sizeDelta = new Vector2(Mathf.Round(22f * Mathf.Clamp01(man.Condition)), 3f);
                fill.GetComponent<Image>().color = man.Condition > 0.6f ? MapPalette.HpGood
                    : man.Condition > 0.3f ? MapPalette.HpFair : MapPalette.HpPoor;
            }

            LayoutActions(actions, 228f);
        }

        readonly System.Text.StringBuilder _facts = new System.Text.StringBuilder(320);

        void Fact(string key, string value)
        {
            _facts.Append(key).Append(": ").Append(string.IsNullOrEmpty(value) ? "-" : value);
            _facts.Append('\n');
        }

        void BuildCrewCard()
        {
            if (_crewCard != null)
                return;

            var body = (RectTransform)_inspectBody.rectTransform.parent;
            _crewCard = New("Crew", body);
            Stretch(_crewCard, 0f, 0f, 0f, 224f);
            _crewCard.SetSiblingIndex(0);

            var mug = New("Mug", _crewCard);
            Pin(mug, TopLeft, Vector2.zero, new Vector2(66f, 80f));
            _crewFace = mug.gameObject.AddComponent<RawImage>();
            _crewFace.raycastTarget = false;
            _crewFace.enabled = false;
            _crewFace.uvRect = Bust(66f, 80f);
            Outline(mug, MapPalette.Rule);

            _crewCaption = Label(_crewCard, "Caption", "LT.", 10f, MapPalette.Strong, 0.08f);
            Pin(_crewCaption.rectTransform, TopLeft, new Vector2(0f, -83f),
                new Vector2(66f, 12f));
            _crewCaption.alignment = TextAlignmentOptions.Center;

            // Ten lines at twelve units each, and the box says so. It was set at a
            // hundred and the tenth line - POSITION - was printing straight through the
            // heading under it, because a rect that is too short does not clip, it just
            // lets the text out of the bottom.
            _crewFacts = Label(_crewCard, "Facts", string.Empty, 10f, MapPalette.Body, 0.04f);
            Stretch(_crewFacts.rectTransform, 74f, 0f, 0f, 132f);
            _crewFacts.alignment = TextAlignmentOptions.TopLeft;
            _crewFacts.lineSpacing = 12f;

            var heading = Label(_crewCard, "Book", "MEN ON THE BOOK", 10f,
                MapPalette.Heading, 0.14f, true);
            Stretch(heading.rectTransform, 0f, 0f, 138f, 12f);

            // Five men at thirteen units - the roster's own ceiling is a lieutenant and
            // four hoods, so the book never needs a sixth row.
            _crewBook = New("Men", _crewCard);
            Stretch(_crewBook, 0f, 0f, 152f, 68f);
        }

        RectTransform BookRowFactory(RectTransform parent, int index)
        {
            var row = New("Man " + index, parent);
            Stretch(row, 0f, 0f, 0f, 12f);

            var dot = Paint(New("Dot", row), Color.white);
            Pin(dot.rectTransform, MiddleLeft, new Vector2(1f, 0f), new Vector2(4f, 4f));

            var name = Label(row, "Name", string.Empty, 9f, MapPalette.Body, 0.04f);
            Stretch(name.rectTransform, 9f, 132f, 0f, 11f);
            name.alignment = TextAlignmentOptions.Left;

            var role = Label(row, "Role", string.Empty, 8f, MapPalette.Muted, 0.04f);
            Pin(role.rectTransform, MiddleRight, new Vector2(-96f, 0f), new Vector2(56f, 11f));
            role.alignment = TextAlignmentOptions.Left;

            var arm = Label(row, "Arm", string.Empty, 8f, MapPalette.UnclaimedChrome, 0.04f);
            Pin(arm.rectTransform, MiddleRight, new Vector2(-28f, 0f), new Vector2(74f, 11f));
            arm.alignment = TextAlignmentOptions.Right;

            var bar = Paint(New("Bar", row), MapPalette.Rule);
            Pin(bar.rectTransform, MiddleRight, new Vector2(-2f, 0f), new Vector2(22f, 3f));
            var fill = Paint(New("Fill", bar.rectTransform), MapPalette.HpGood);
            Pin(fill.rectTransform, MiddleLeft, Vector2.zero, new Vector2(22f, 3f));
            return row;
        }

        /// <summary>Ask the studio for a face. The image stays dark until the print
        /// lands, which is one frame for a face nobody has asked for before and instant
        /// for every one after - the studio caches by prefab, which is exactly the
        /// stable key the design sheet's `mug` slug is.</summary>
        static void Face(RawImage into, GameObject mug)
        {
            if (into == null)
                return;
            if (mug == null)
            {
                into.enabled = false;
                into.texture = null;
                return;
            }
            LivingCity.UI.PortraitStudio.Request(mug,
                LivingCity.UI.PortraitStudio.Framing.Bust, into);
        }

        /// <summary>A square print cropped to a taller window, held at the head line so
        /// the crop takes the shoulders rather than the face.</summary>
        static Rect Bust(float width, float height)
        {
            if (width >= height)
            {
                var h = height / Mathf.Max(width, 1f);
                var y = Mathf.Clamp(0.70f - h * 0.5f, 0f, 1f - h);
                return new Rect(0f, y, 1f, h);
            }
            var w = width / Mathf.Max(height, 1f);
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }

        // ------------------------------------------------------------------ the log

        public void SetLog(IReadOnlyList<string> lines)
        {
            for (var i = 0; i < _logLines.Count; i++)
                _logLines[i].text = i < lines.Count ? lines[i] : string.Empty;
        }

        // ----------------------------------------------------------------- the menu

        /// <summary>One row of the order card: what it is, what it does, and whether it
        /// can be done at all. A row that cannot be taken still STANDS, faded, with the
        /// reason where the note goes - the city's own card does this, and it is why a
        /// player who has bought no motorcycle can see that a drive-by exists and why he
        /// cannot order one.</summary>
        public struct MenuRow
        {
            public string Label;
            public string Note;
            public bool Lit;
        }

        /// <summary>Opens the order card at a point of the MAP, clamped so it can never
        /// run off it - the sheet's own 72 and 62 percent.</summary>
        public void ShowMenu(Vector2 fraction, string title, List<MenuRow> rows)
        {
            if (_menu == null)
                return;

            _menuTitle.text = title;
            Grow(_menuPool, _menuItems, rows.Count, MenuItemFactory);

            var width = 210f;
            for (var i = 0; i < _menuPool.Count; i++)
            {
                var item = _menuPool[i];
                item.gameObject.SetActive(i < rows.Count);
                if (i >= rows.Count)
                    continue;

                var row = rows[i];
                var label = item.Find("Label").GetComponent<TMP_Text>();
                var note = item.Find("Note").GetComponent<TMP_Text>();
                label.text = "\u25B8 " + row.Label;
                note.text = row.Note ?? string.Empty;
                label.color = row.Lit ? MapPalette.MenuText : MapPalette.Dim;
                note.color = row.Lit ? MapPalette.Muted : MapPalette.Dim;

                width = Mathf.Max(width,
                    Mathf.Max(label.GetPreferredValues(label.text).x,
                              note.GetPreferredValues(note.text).x) + 26f);
                Stretch(item, 0f, 0f, i * MenuRowHeight, MenuRowHeight - 2f);

                var index = i;
                var button = item.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                if (row.Lit)
                    button.onClick.AddListener(() => OnMenuItem?.Invoke(index));
            }

            var map = _surface.rect.size;
            Pin(_menu, TopLeft,
                new Vector2(Mathf.Min(fraction.x, 0.72f) * map.x,
                            -Mathf.Min(fraction.y, 0.62f) * map.y),
                new Vector2(width, MenuTitleHeight + rows.Count * MenuRowHeight + 5f));
            _menu.gameObject.SetActive(true);
            _menu.SetAsLastSibling();
        }

        public void HideMenu()
        {
            if (_menu != null)
                _menu.gameObject.SetActive(false);
        }

        RectTransform MenuItemFactory(RectTransform parent, int index)
        {
            var item = New("Item " + index, parent);
            Stretch(item, 0f, 0f, 0f, MenuRowHeight - 2f);
            var face = item.gameObject.AddComponent<Image>();
            face.color = new Color(1f, 1f, 1f, 0f);

            var button = item.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = new Color(1f, 1f, 1f, 0f);
            colours.highlightedColor = MapPalette.MenuHover;
            colours.pressedColor = MapPalette.MenuHover;
            colours.selectedColor = new Color(1f, 1f, 1f, 0f);
            colours.fadeDuration = 0f;
            button.colors = colours;

            var label = Label(item, "Label", string.Empty, 11f, MapPalette.MenuText, 0.10f);
            Stretch(label.rectTransform, 10f, 10f, 3f, 13f);
            label.alignment = TextAlignmentOptions.Left;

            var note = Label(item, "Note", string.Empty, 10f, MapPalette.Muted, 0.04f);
            Stretch(note.rectTransform, 10f, 10f, 15f, 12f);
            note.alignment = TextAlignmentOptions.Left;
            return item;
        }

        // ------------------------------------------------------------- on-map labels

        public struct MapLabel
        {
            /// <summary>Across and down the map, nought to one.</summary>
            public Vector2 Fraction;

            public string Text;
            public Color Colour;

            /// <summary>Clear for a plain place name; a colour makes it a chip in that
            /// family's border, with a swatch.</summary>
            public Color Border;
        }

        /// <summary>
        /// The lettering over the map: a chip per district while the turf overlay is on,
        /// plain place names when it is off - the sheet's own swap. Sized as a fraction
        /// of the map's width, so it grows with the picture.
        ///
        /// The chips hold direct references to their own parts rather than looking them
        /// up by name. This runs on every frame the map moves, which is most of them,
        /// and a Find per part per chip per frame is both waste and a null waiting to
        /// happen the first time a part is renamed.
        /// </summary>
        public void SetLabels(List<MapLabel> labels)
        {
            if (_surface == null || labels == null)
                return;

            while (_labelPool.Count < labels.Count)
                _labelPool.Add(MakeChip(_labelPool.Count));

            var type = Mathf.Clamp(_surface.rect.width * 0.0062f, 7f, 22f);

            for (var i = 0; i < _labelPool.Count; i++)
            {
                var chip = _labelPool[i];
                var on = i < labels.Count;
                if (chip.Rect.gameObject.activeSelf != on)
                    chip.Rect.gameObject.SetActive(on);
                if (!on)
                    continue;

                var data = labels[i];
                var words = data.Text ?? string.Empty;
                var chipped = data.Border.a > 0.01f;

                // Measuring a string is not free. The words are measured only when they
                // or the type size have actually changed; WHERE the chip sits is worked
                // out every frame, because the ground under it is moving.
                if (chip.Words != words || !Mathf.Approximately(chip.Type, type) ||
                    chip.Chipped != chipped)
                {
                    chip.Words = words;
                    chip.Type = type;
                    chip.Chipped = chipped;
                    chip.Text.text = words;
                    chip.Text.fontSize = type;
                    chip.Width = chip.Text.GetPreferredValues(words).x;
                    chip.Face.color = chipped ? MapPalette.Chip : Clear;
                    chip.Border.color = chipped ? data.Border : Clear;
                    chip.Swatch.gameObject.SetActive(chipped);
                    Fill(chip.TextRect, chipped ? 17f : 4f, 4f, 0f, 0f);
                    chip.Rect.sizeDelta =
                        new Vector2(chip.Width + (chipped ? 25f : 8f), type + 8f);
                }

                chip.Text.color = data.Colour;
                if (chipped)
                    chip.Swatch.color = data.Border;

                chip.Rect.anchorMin = chip.Rect.anchorMax =
                    new Vector2(data.Fraction.x, 1f - data.Fraction.y);
                chip.Rect.pivot = Middle;
                chip.Rect.anchoredPosition = Vector2.zero;
            }
        }

        sealed class LabelChip
        {
            public RectTransform Rect;
            public RectTransform TextRect;
            public Image Face;
            public Image Border;
            public Image Swatch;
            public TMP_Text Text;

            public string Words;
            public float Type = -1f;
            public float Width;
            public bool Chipped;
        }

        LabelChip MakeChip(int index)
        {
            var rect = New("Label " + index, _labels);
            var face = rect.gameObject.AddComponent<Image>();
            face.color = MapPalette.Chip;
            face.raycastTarget = false;
            var border = Outline(rect, Clear);

            var swatch = Paint(New("Swatch", rect), Color.white);
            Pin(swatch.rectTransform, MiddleLeft, new Vector2(6f, 0f), new Vector2(7f, 7f));

            var text = Label(rect, "Label", string.Empty, 11f, Color.white, 0.14f);
            Fill(text.rectTransform, 17f, 4f, 0f, 0f);
            text.alignment = TextAlignmentOptions.Center;

            return new LabelChip
            {
                Rect = rect,
                TextRect = text.rectTransform,
                Face = face,
                Border = border,
                Swatch = swatch,
                Text = text,
            };
        }

        // ------------------------------------------------------------------- scrap

        public void Release()
        {
            if (_scanlines != null) Object.Destroy(_scanlines);
            if (_vignette != null) Object.Destroy(_vignette);
            if (_frame != null && _frame.texture != null) Object.Destroy(_frame.texture);
            _scanlines = null;
            _vignette = null;
            _frame = null;
        }

        // ---------------------------------------------------------------- plumbing

        static void Grow(List<RectTransform> pool, RectTransform parent, int wanted,
            System.Func<RectTransform, int, RectTransform> make)
        {
            while (pool.Count < wanted)
                pool.Add(make(parent, pool.Count));
        }

        static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>Stretched across its parent and hung from the top: insets left and
        /// right, an inset from the top, and a height. Every stacked thing on this
        /// terminal is one of these, which is why a stack can be laid by adding up
        /// heights and never by reading a rect back.</summary>
        static void Stretch(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = TopLeft;
            rect.anchorMax = TopRight;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>A fixed size pinned to one point of its parent. The offset is read in
        /// the pivot's own directions, which is why a top-right pin takes a negative
        /// x.</summary>
        static void Pin(RectTransform rect, Vector2 corner, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        static void Fill(RectTransform rect, float left = 0f, float right = 0f,
            float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Middle;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static Image Paint(RectTransform rect, Color colour)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A line of the terminal's own words.
        ///
        /// Set in the city's own screen face and NOT in the map's pixel font. The design
        /// sheet puts Silkscreen on everything, and that is right for a page which is
        /// entirely a picture of a 1987 terminal - but here the overlays stand on a
        /// living map, at a size the sheet never anticipated, and a bitmap face blown up
        /// over it reads as a blurred imitation of pixel art rather than as pixel art.
        /// The RASTER is the pixels; the chrome is type, the same type every other
        /// screen in this game is set in.
        /// </summary>
        TMP_Text Label(Transform parent, string name, string text, float size, Color colour,
            float spacing, bool heading = false)
        {
            var rect = New(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var face = heading
                ? LivingCity.UI.LedgerStyle.Condensed
                : LivingCity.UI.LedgerStyle.CondensedText;
            if (face != null)
                label.font = face;
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.characterSpacing = spacing * 100f;
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>A one-unit border, drawn as a nine-sliced sprite so it stays one unit
        /// whatever the rect does. Every rule on this terminal is one of these, and each
        /// is the LAST child of its rect, so it prints over the panel's own contents
        /// rather than under them.</summary>
        Image Outline(RectTransform rect, Color colour, float thickness = 1f)
        {
            var border = New("Border", rect);
            Fill(border);
            var image = border.gameObject.AddComponent<Image>();
            image.sprite = FrameSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, thickness);
            image.color = colour;
            image.raycastTarget = false;
            border.SetAsLastSibling();
            return image;
        }

        static void Recolour(RectTransform rect, Color colour)
        {
            var border = rect.Find("Border");
            if (border != null && border.TryGetComponent<Image>(out var image))
                image.color = colour;
        }

        Sprite FrameSprite()
        {
            if (_frame != null)
                return _frame;
            // Bilinear, not point: this rule is chrome and not part of the picture, so
            // it wants a clean hairline at any scale rather than the stepped edge a
            // point-sampled texel gives when the canvas is not at 1:1.
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                name = "Terminal Rule",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 1 && y == 1 ? Color.clear : Color.white);
            texture.Apply();
            // A hundred pixels to the unit, matching the canvas's own reference: a
            // sliced border is scaled by referencePixelsPerUnit / sprite.pixelsPerUnit,
            // so a sprite cut at one would draw this one-pixel rule a HUNDRED units
            // thick and swallow every small rect it was put round.
            _frame = Sprite.Create(texture, new Rect(0f, 0f, 3f, 3f), Middle, 100f, 0,
                SpriteMeshType.FullRect, new Vector4(1f, 1f, 1f, 1f));
            _frame.name = "Terminal Rule";
            return _frame;
        }
    }
}
