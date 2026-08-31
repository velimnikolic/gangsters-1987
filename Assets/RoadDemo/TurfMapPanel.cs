using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.UI;

namespace RoadDemo
{
    /// <summary>
    /// The one visual vocabulary for pointer menus over both the TurfMap and the street.
    /// Their input paths deliberately differ (uGUI buttons on the map, manual rectangles
    /// over the 3D view), but the paper, type, rules and hover wash must not drift apart.
    /// </summary>
    internal static class TurfContextMenuStyle
    {
        public const float HeaderHeight = 22f;
        public const float EnemyWidth = 268f;
        public const float EnemyRowHeight = 34f;
        public const float FooterHeight = 4f;

        public static readonly Color Paper = new Color32(247, 240, 218, 247);
        public static readonly Color Border = new Color32(43, 36, 24, 140);
        public static readonly Color Rule = new Color32(43, 36, 24, 90);
        public static readonly Color Body = new Color32(47, 40, 32, 255);
        public static readonly Color Note = new Color32(109, 92, 64, 255);
        public static readonly Color Disabled = new Color32(138, 119, 86, 170);
        public static readonly Color Accent = new Color32(143, 33, 25, 255);
        public static readonly Color Hover = new Color32(143, 33, 25, 31);
        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        public static void Dress(RectTransform rect)
        {
            LedgerKit.Fill(rect, Paper);
            LedgerKit.Frame(rect, 0.5f, Border);
        }

        public static void ClearContent(RectTransform rect)
        {
            // LedgerKit.Frame is four anchored "Edge" children. They are permanent
            // chrome; only the heading and action rows belong to one opening.
            for (int i = rect.childCount - 1; i >= 0; i--)
                if (rect.GetChild(i).name != "Edge")
                    Object.Destroy(rect.GetChild(i).gameObject);
        }

        public static TextMeshProUGUI Header(Transform parent, float width, string label)
        {
            const float size = 10f;
            float y = -(HeaderHeight - LedgerKit.LineBox(size)) * 0.5f;
            var title = LedgerKit.Line(parent, LedgerStyle.Condensed, size, Accent,
                10f, y, width - 16f, LedgerKit.LineBox(size),
                label != null ? label.ToUpperInvariant() : string.Empty);
            title.characterSpacing = 16f;
            title.overflowMode = TextOverflowModes.Ellipsis;
            LedgerKit.Rule(parent, 0f, -HeaderHeight + 1f, width, Rule, 1f);
            return title;
        }

        public static void EnemyText(RectTransform row, float width,
            out TextMeshProUGUI label, out TextMeshProUGUI note)
        {
            label = LedgerKit.Line(row, LedgerStyle.Mono, 11f, Body,
                11f, 0f, width - 16f, EnemyRowHeight * 0.56f, string.Empty);
            label.overflowMode = TextOverflowModes.Ellipsis;

            note = LedgerKit.Line(row, LedgerStyle.Mono, 9.5f, Note,
                11f, -EnemyRowHeight * 0.48f, width - 16f,
                EnemyRowHeight * 0.44f, string.Empty);
            note.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>
    /// The map's paper: the date plate, the ONE panel top left, the turf key pinned
    /// to the bottom right, the place labels over the districts, and the right-click
    /// menu. Nothing else ever floats over the plate.
    ///
    /// The panel is one scroll and one column. When a lieutenant is picked his file
    /// takes the panel's top section and the roster sits under it beyond a 2 px rule -
    /// it is not a second screen and not a tab, and the whole thing scrolls together,
    /// so a long dossier pushes the roster off the bottom exactly the way a sheet of
    /// paper would. The panel is only as tall as what is on it; the max-height is a
    /// limit, never a size.
    ///
    /// The file simply APPEARS. There was an unroll here - the section growing from
    /// nothing, the contents dropping in rotated, the mug wiped down, the lines
    /// arriving in steps - and it was taken out on purpose: a panel that repaints
    /// whenever the clock ticks or a man is hit cannot animate its own arrival without
    /// re-running the arrival on top of itself, and the file spent as much time sliding
    /// as standing still.
    ///
    /// EVERY WIDTH ON THIS PANEL IS MEASURED, never counted off the letters. A
    /// character count times a guessed pitch is what put a button's frame through its
    /// own words and clipped the roster's title to "CREWS AFIELD · LI…"; the face knows
    /// exactly what it sets, so it is asked - see <see cref="Wide"/>.
    ///
    /// All type goes through LedgerStyle, the project's single source for faces:
    /// Condensed is the Oswald the design letters everything in, Mono is the
    /// typewriter the file bodies are set in.
    /// </summary>
    public sealed class TurfMapPanel : MonoBehaviour
    {
        // ------------------------------------------------------------- the paper

        static readonly Color PlateSolid = new Color32(244, 236, 214, 250);
        static readonly Color Hairline = new Color32(43, 36, 24, 140);
        static readonly Color Rule = new Color32(43, 36, 24, 90);
        static readonly Color RuleFaint = new Color32(43, 36, 24, 40);
        static readonly Color Ink = new Color32(43, 36, 24, 255);
        static readonly Color Body = new Color32(47, 40, 32, 255);
        static readonly Color Red = new Color32(143, 33, 25, 255);
        static readonly Color Label = new Color32(109, 92, 64, 255);
        static readonly Color Dim = new Color32(138, 119, 86, 255);
        static readonly Color Slate = new Color32(59, 50, 38, 255);
        static readonly Color Well = new Color32(43, 36, 24, 16);
        static readonly Color MugField = new Color32(224, 212, 182, 255);

        // Leave a small strip of map paper between the clock controls and the file.
        const float PanelLeft = 0f, PanelTop = DemoClockHud.Height + 8f, PanelFoot = 24f;
        const float PanelWidthFraction = 0.18f;
        const float Pad = 10f, HeadPad = 11f;

        /// <summary>The design's floor for panel type. Anything under it stops being
        /// a label and becomes texture.</summary>
        const float MicroType = 8f;

        const float MugHeight = 96f;
        const float CloseBox = 22f, CloseGlyph = 18f;

        /// <summary>Every row on the panel that carries a label and a value - a stat, a
        /// roster line, a button - is this tall, and everything inside one is centred on
        /// its middle. One number instead of a y per element is the whole of what keeps
        /// a column looking like a column.</summary>
        const float StatHeight = 17f, ButtonHeight = 22f, RosterHeight = 42f;

        TurfMapHud _hud;
        Canvas _canvas;
        bool _showPanel, _showMapChrome;

        RectTransform _panelRect, _viewport, _content, _keyRect, _placesRoot, _menuRect;
        DemoCrews.Unit _menuActor, _menuTarget;

        RectTransform _dossierRect;
        RawImage _mugImage;

        /// <summary>Connects a roster thumbnail to the CrewBar's one shared, rotating
        /// camera feed. It deliberately owns no camera or RenderTexture: moving the
        /// picture into the permanent paper panel must not double the cost of filming
        /// the same lieutenant.</summary>
        sealed class LieutenantLiveFeed : MonoBehaviour
        {
            public DemoCrews.Unit Unit;
            RawImage _image;

            void Awake() => _image = GetComponent<RawImage>();

            void LateUpdate()
            {
                if (CrewBar.Instance != null &&
                    CrewBar.Instance.TryGetFeed(Unit, out var feed))
                {
                    if (_image.texture != feed)
                        _image.texture = feed;
                    _image.enabled = true;
                    return;
                }

                _image.enabled = false;
            }
        }

        int _paintedStamp = -1;
        float _paintedPanelWidth = -1f;
        float _scroll;

        public void Init(TurfMapHud owner, bool showPanel, bool showMapChrome)
        {
            _hud = owner;
            _canvas = GetComponent<Canvas>();
            _showPanel = showPanel;
            _showMapChrome = showMapChrome;
            Build();
        }

        /// <summary>Eighteen percent of the window in CANVAS units, which is what a sizeDelta
        /// is. Screen.width is a count of real pixels, and handing one straight to a
        /// scaled canvas is how the panel ended up a third of its intended size on a
        /// small game view.</summary>
        float PanelWidth =>
            Screen.width / (_canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : _hud.UiScale) *
            PanelWidthFraction;

        // ------------------------------------------------------------------ build

        void Build()
        {
            var root = (RectTransform)transform;
            BuildRuler(root);

            if (_showPanel)
                BuildPanel(root);
            if (_showMapChrome)
                BuildMapChrome(root);
        }

        void BuildPanel(RectTransform root)
        {
            _panelRect = Paper("Panel", root, PlateSolid, borderThickness: 0.5f);
            Anchor(_panelRect, 0f, 1f, PanelLeft, -PanelTop);

            _viewport = DemoUi.NewRect("Viewport", _panelRect);
            DemoUi.Fill(_viewport);
            _viewport.gameObject.AddComponent<RectMask2D>();

            _content = DemoUi.NewRect("Content", _viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);
        }

        void BuildMapChrome(RectTransform root)
        {
            _placesRoot = DemoUi.NewRect("Places", root);
            DemoUi.Fill(_placesRoot);
            _placesRoot.SetAsFirstSibling();

            BuildKey(root);
            _paintedKey = KeyStamp();

            _menuRect = DemoUi.NewRect("Menu", root);
            TurfContextMenuStyle.Dress(_menuRect);
            _menuRect.gameObject.SetActive(false);
        }

        // ----------------------------------------------------------------- the ruler

        TextMeshProUGUI _ruler;

        void BuildRuler(RectTransform root)
        {
            _ruler = LedgerKit.Line(root, LedgerStyle.Condensed, 10f, Ink, 0f, 0f, 400f, 20f, "");
            _ruler.gameObject.SetActive(false);
        }

        /// <summary>
        /// What the face actually sets for these words, at this size, with this
        /// tracking. Every width on the panel comes from here.
        ///
        /// The alternative - a character count times a guessed pitch - was wrong in both
        /// directions at once on the same row: BACK TO CREWS overran the frame it was
        /// given while TURF ON rattled around inside its own, and the roster's title was
        /// clipped to make room for space the buttons were not using.
        /// </summary>
        float Wide(string text, float size, TMP_FontAsset font, float tracking)
        {
            if (_ruler == null || string.IsNullOrEmpty(text))
                return 0f;

            _ruler.font = font;
            _ruler.fontSize = size;
            _ruler.characterSpacing = tracking;
            _ruler.text = text;

            // TMP puts the tracking after the LAST glyph too, which on a right-aligned
            // reading is a phantom letter of margin. Taken back off here so a measured
            // box is the box the words fill.
            return Mathf.Max(0f, _ruler.GetPreferredValues(text).x - size * tracking * 0.01f);
        }

        /// <summary>A fill that CATCHES clicks. LedgerKit's own fill is deliberately
        /// transparent to the raycaster - the book has nothing clickable on it - so a
        /// button built out of one would look right and do nothing.</summary>
        static Image Clickable(RectTransform rect, Color face)
        {
            var image = LedgerKit.Fill(rect, face);
            image.raycastTarget = true;
            return image;
        }

        /// <summary>A sheet edge is a single physical-pixel rule at the map's normal
        /// scale. Heavier rules are reserved for controls inside the sheet.</summary>
        static RectTransform Paper(string name, Transform parent, Color face,
            float borderThickness = 1f)
        {
            var rect = DemoUi.NewRect(name, parent);
            LedgerKit.Fill(rect, face);
            LedgerKit.Frame(rect, borderThickness, Hairline);
            return rect;
        }

        static void Anchor(RectTransform rect, float ax, float ay, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(ax, ay);
            rect.pivot = new Vector2(ax, ay);
            rect.anchoredPosition = new Vector2(x, y);
        }

        static TextMeshProUGUI Caps(Transform parent, float x, float y, float w,
            string label, float size, Color colour, TMP_FontAsset font,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var text = LedgerKit.Line(parent, font, size, colour, x, y, w,
                LedgerKit.LineBox(size), label, align);
            text.characterSpacing = 10f;
            return text;
        }

        /// <summary>
        /// The turf key: one horizontal strip nailed into the bottom right corner of
        /// the map, with only its top and left edges drawn - a tab torn off the sheet
        /// rather than a floating card. The map fills the viewport, so the map's
        /// corner and the screen's are the same corner.
        ///
        /// The wash's own switch lives HERE, at the head of the strip, and not up in the
        /// panel: this is the one place on the screen that shows what the wash is FOR -
        /// which colour belongs to which family - so it is the place the player is
        /// already looking when he wants it off.
        ///
        /// Rebuilt whenever the switch moves or a family appears on the ground, because
        /// the roll of houses is read off the survey and the survey does not exist yet
        /// when the panel is first built.
        /// </summary>
        void BuildKey(RectTransform root)
        {
            const float tall = 24f;      // the strip's own height
            const float pad = 10f;       // the same breath at both ends
            const float swatchW = 13f, swatchH = 9f;
            const float chipToWord = 6f; // a swatch and its name are one thing
            const float betweenHouses = 14f;

            if (_keyRect == null)
            {
                _keyRect = DemoUi.NewRect("Turf Key", root);
                _keyRect.anchorMin = _keyRect.anchorMax = new Vector2(1f, 0f);
                _keyRect.pivot = new Vector2(1f, 0f);
                _keyRect.anchoredPosition = Vector2.zero;
                LedgerKit.Fill(_keyRect, new Color32(247, 240, 218, 230));
            }
            else
            {
                for (int i = _keyRect.childCount - 1; i >= 0; i--)
                    Destroy(_keyRect.GetChild(i).gameObject);
            }

            // Everything on the strip is centred on the strip's own middle line, and
            // that line is worked out from each item's own height. Handing each
            // element a y of its own is what put the words three units under the
            // swatches they name.
            float x = pad;

            const float switchH = 16f;
            string switchLabel = _hud.TurfOn ? "TURF ON" : "TURF OFF";
            float switchWide = Wide(switchLabel, 9f, LedgerStyle.Condensed, 10f) + 18f;
            SmallButton(_keyRect, x, Mid(tall, switchH), switchWide, switchH, switchLabel,
                _hud.TurfOn, () => _hud.SetTurf(!_hud.TurfOn));
            x += switchWide + 9f;

            const float ruleTall = 12f;
            LedgerKit.VRule(_keyRect, x, Mid(tall, ruleTall), ruleTall, RuleFaint, 1f);
            x += 11f;

            float wordBox = LedgerKit.LineBox(9f);
            foreach (var house in KeyHouses())
            {
                var swatch = DemoUi.NewRect("Swatch", _keyRect);
                LedgerKit.PlaceTopLeft(swatch, x, Mid(tall, swatchH), swatchW, swatchH);
                LedgerKit.Fill(swatch, (Color32)house.Wash);
                LedgerKit.Frame(swatch, 1f, house.Ink);
                x += swatchW + chipToWord;

                float wide = Wide(house.Short, 9f, LedgerStyle.Condensed, 10f);
                Caps(_keyRect, x, Mid(tall, wordBox), wide + 2f, house.Short, 9f, Slate,
                    LedgerStyle.Condensed);
                x += wide + betweenHouses;
            }

            x += pad - betweenHouses;   // the last house is followed by the end, not a gap
            _keyRect.sizeDelta = new Vector2(x, tall);

            // Only the two edges that face into the map: the strip is nailed into the
            // corner, not floating in it, so the corner's own two sides carry no rule.
            LedgerKit.Rule(_keyRect, 0f, 0f, _keyRect.sizeDelta.x, Hairline, 1f);
            LedgerKit.VRule(_keyRect, 0f, 0f, _keyRect.sizeDelta.y, Hairline, 1f);
        }

        /// <summary>What the key is showing: the switch and the roll of families with
        /// ground. The survey names them, and it is drawn after this panel is built, so
        /// the strip cannot be built once and left.</summary>
        int KeyStamp()
        {
            // Read straight off the districts rather than through KeyHouses: this runs
            // every frame the map is up, and the roll builds a set to dedupe with.
            int stamp = _hud.TurfOn ? 1 : 0;
            if (_hud.Survey == null)
                return stamp;
            foreach (var district in _hud.Survey.Districts)
                stamp = stamp * 31 + district.GangId + 3;
            return stamp;
        }

        int _paintedKey = -1;

        /// <summary>The top offset that centres a box of the given height inside a
        /// row of the given height. One rule for every element on a strip, so type and
        /// graphics sit on the same line.</summary>
        static float Mid(float rowTall, float itemTall) => -(rowTall - itemTall) * 0.5f;

        /// <summary>The top a box of this height needs to sit centred on a line at this
        /// depth. For rows that carry TWO lines, where a box each would stack past the
        /// bottom of the row.</summary>
        static float On(float centreY, float box) => centreY + box * 0.5f;

        /// <summary>Ours, the families actually holding ground, unclaimed and
        /// contested. A key that listed all twenty houses would be a second panel.
        /// </summary>
        IEnumerable<TurfHouse> KeyHouses()
        {
            yield return TurfHouses.Ours;

            var seen = new HashSet<int>();
            if (_hud != null && _hud.Survey != null)
                foreach (var district in _hud.Survey.Districts)
                    if (district.GangId > 0 && seen.Add(district.GangId))
                        yield return TurfHouses.For(district.GangId);

            yield return TurfHouses.Contested;
            yield return TurfHouses.Unclaimed;
        }

        // ---------------------------------------------------------------- refresh

        // Bump when the runtime-built row shape changes, so an in-Play script reload
        // rebuilds existing paper instead of leaving the pre-change controls standing.
        const int PanelLayoutVersion = 3;

        public void Refresh()
        {
            if (_showPanel)
            {
                float width = PanelWidth;
                if (Mathf.Abs(width - _paintedPanelWidth) > 0.1f)
                {
                    _paintedPanelWidth = width;
                    _paintedStamp = -1;
                }

                int stamp = Stamp();
                if (stamp != _paintedStamp)
                {
                    _paintedStamp = stamp;
                    Repaint();
                }
                Scroll();
            }

            if (_showMapChrome)
            {
                if (_menuTarget != null &&
                    !_hud.EnemyContextValid(_menuActor, _menuTarget))
                    CloseMenu();

                int key = KeyStamp();
                if (key != _paintedKey)
                {
                    _paintedKey = key;
                    BuildKey((RectTransform)transform);
                }
                Places();
            }
        }

        /// <summary>What the panel is showing. Changing it repaints; a frame that
        /// changed nothing costs one integer.</summary>
        int Stamp()
        {
            int stamp = PanelLayoutVersion;
            stamp = stamp * 31 + (_hud.TurfOn ? 1 : 0);
            stamp = stamp * 31 + (_hud.InspectedCrew != null ? _hud.InspectedCrew.Id + 7 : 0);
            stamp = stamp * 31 + (_hud.InspectedBuilding != null ? _hud.InspectedBuilding.Id + 11 : 0);
            stamp = stamp * 31 + (_hud.InspectedDistrict != null
                ? _hud.InspectedDistrict.Name.GetHashCode() : 0);
            // WHICH crews are gathered, not how many: a marquee thrown over a different
            // pair of the same size changes nothing in this number, and the roster's
            // red edge would stay on the crews it was on before.
            foreach (var crew in _hud.Units)
            {
                var boss = crew.Unit != null ? crew.Unit.Boss : null;
                stamp = stamp * 31 + (int)crew.Order + crew.MenStanding * 13 +
                        crew.HoodsOnBooks * 17 + (boss != null ? boss.CharacterId * 19 : 0) +
                        (_hud.IsGathered(crew.Id) ? 7 : 0);
            }
            return stamp;
        }

        public void SelectionChanged() => _paintedStamp = -1;

        void Scroll()
        {
            var mouse = Mouse.current;
            if (mouse != null && ClaimsPointer(mouse.position.ReadValue()))
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                    _scroll -= wheel * 0.4f;
            }

            float max = Mathf.Max(0f, _content.sizeDelta.y - _panelRect.sizeDelta.y);
            _scroll = Mathf.Clamp(_scroll, 0f, max);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        // ---------------------------------------------------------------- repaint

        void Repaint()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            _dossierRect = null;
            _mugImage = null;

            float width = PanelWidth;
            _panelRect.sizeDelta = new Vector2(width, 0f);
            _content.sizeDelta = new Vector2(0f, 0f);

            float y = 0f;
            if (_hud.InspectedCrew != null)
                y -= BuildDossier(_hud.InspectedCrew, width, y);

            y -= BuildHeader(width, y);

            if (_hud.InspectedBuilding != null)
                y -= BuildPropertyFile(_hud.InspectedBuilding, width, y);
            else if (_hud.InspectedDistrict != null)
                y -= BuildDistrictFile(_hud.InspectedDistrict, width, y);
            else
                y -= BuildRoster(width, y);

            _content.sizeDelta = new Vector2(0f, -y);

            float max = Screen.height / _hud.UiScale - PanelTop - PanelFoot;
            _panelRect.sizeDelta = new Vector2(width, Mathf.Min(-y, max));
        }

        // --------------------------------------------------------------- dossier

        float BuildDossier(TurfCrew crew, float width, float top)
        {
            _dossierRect = DemoUi.NewRect("Dossier", _content);
            _dossierRect.anchorMin = new Vector2(0f, 1f);
            _dossierRect.anchorMax = new Vector2(1f, 1f);
            _dossierRect.pivot = new Vector2(0.5f, 1f);
            _dossierRect.offsetMin = new Vector2(0f, 0f);
            _dossierRect.offsetMax = new Vector2(0f, top);

            float y = -8f;
            float inner = width - Pad * 2f;

            // The title and the cross are ONE line, so they are centred on one line.
            // The cross used to be placed off the title's top edge and a box twice the
            // title's height hung eight units below it.
            const float titleRow = 24f;
            Caps(_dossierRect, Pad, y + Mid(titleRow, LedgerKit.LineBox(10f)),
                inner - CloseBox - 4f, "PERSONAL FILE", 10f, Slate,
                LedgerStyle.Condensed);

            var close = DemoUi.NewRect("Close", _dossierRect);
            LedgerKit.PlaceTopLeft(close, width - Pad - CloseBox,
                y + Mid(titleRow, CloseBox), CloseBox, CloseBox);
            var closeFace = Clickable(close, new Color(0f, 0f, 0f, 0f));
            var closeGlyph = LedgerKit.Line(close, LedgerStyle.Condensed, CloseGlyph, Red,
                0f, 0f, CloseBox, CloseBox, "×", TextAlignmentOptions.Center);
            LedgerKit.RowButton(close, closeFace, _hud.ClearInspection);
            Hover.Add(close, closeFace, closeGlyph, Red, new Color32(242, 230, 204, 255));

            y -= titleRow;
            LedgerKit.Rule(_dossierRect, 0f, y, width, Rule, 1f);
            y -= Pad;

            var mugFrame = DemoUi.NewRect("Mug", _dossierRect);
            LedgerKit.PlaceTopLeft(mugFrame, Pad, y, inner, MugHeight);
            LedgerKit.Fill(mugFrame, MugField);
            LedgerKit.Frame(mugFrame, 1f, Hairline);

            var mugRect = DemoUi.NewRect("Print", mugFrame);
            LedgerKit.PlaceTopLeft(mugRect, 1f, -1f, inner - 2f, MugHeight - 2f);
            _mugImage = mugRect.gameObject.AddComponent<RawImage>();
            _mugImage.enabled = false;
            _mugImage.raycastTarget = false;

            // Prefer the exact prefab walking outside. If the unit has not been dealt
            // yet, ask the same MemberModel door the street spawn uses; never resolve a
            // second, merely similar face from a copied string.
            var body = crew.Unit != null && crew.Unit.Boss != null
                ? crew.Unit.Boss.SourcePrefab
                : crew.Lieutenant != null ? PersonnelAlmanac.MemberModel(crew.Lieutenant) : null;
            if (body != null)
                // The map dossier is the same personnel file viewed from the plan:
                // use the ledger's full-colour bust, not the newspaper halftone.
                PortraitStudio.Request(body, PortraitStudio.Framing.Bust, _mugImage);
            else
                Caps(mugFrame, 0f, Mid(MugHeight, LedgerKit.LineBox(MicroType)), inner,
                    "NO PRINT ON FILE", MicroType, Dim, LedgerStyle.Condensed,
                    TextAlignmentOptions.Center);

            y -= MugHeight + 8f;

            LedgerKit.Line(_dossierRect, LedgerStyle.Condensed, 15f, Ink,
                Pad, y, inner, LedgerKit.LineBox(15f), crew.Name)
                .overflowMode = TextOverflowModes.Ellipsis;
            y -= LedgerKit.LineBox(15f);

            // The typewritten kicker is the one line here that can run to any length -
            // a rank and a family - so it is set without extra tracking and cut rather
            // than allowed to run off the paper. Character has no alias field, so this
            // must not invent one by repeating the unit's name.
            // A rival's lieutenant is on nobody's books and has no rank to print, so
            // the line starts at his family rather than at a blank.
            var house = TurfHouses.For(crew.GangId);
            var sub = LedgerKit.Line(_dossierRect, LedgerStyle.Mono, 9f, Red, Pad, y, inner,
                LedgerKit.LineBox(9f),
                (string.IsNullOrEmpty(crew.Rank) ? "" : crew.Rank + " · ") + house.Short);
            sub.overflowMode = TextOverflowModes.Ellipsis;
            y -= LedgerKit.LineBox(9f) + 6f;

            // These are the very same half-step ratings shown in the ledger, in the
            // ledger's block meter. The map is a view onto that book, not a second
            // character sheet with its own invented stat names.
            y -= SkillRow(_dossierRect, Pad, y, inner, "Intelligence", crew.Intelligence);
            y -= SkillRow(_dossierRect, Pad, y, inner, "Organization", crew.Organization);
            y -= SkillRow(_dossierRect, Pad, y, inner, "Firearms", crew.Firearms);
            y -= CrewCountRow(_dossierRect, Pad, y, inner, crew);

            y -= 5f;
            Caps(_dossierRect, Pad, y, inner, "CARRYING", MicroType, Label,
                LedgerStyle.Condensed);
            y -= LedgerKit.LineBox(MicroType) + 2f;

            y -= KitBox(_dossierRect, Pad, y, inner, crew.Gun);
            y -= KitBox(_dossierRect, Pad, y, inner, crew.Ride);

            y -= 6f;

            // One grid for all six: three rows of two, the same gutter down the middle
            // and the same step down. The two icon buttons used to be a different height
            // from the four word buttons above them and stepped by a different amount.
            const float gutter = 4f, step = ButtonHeight + 4f;
            float half = (inner - gutter) * 0.5f;
            float rightX = Pad + half + gutter;

            if (crew.Mine)
            {
                Action(_dossierRect, Pad, y, half, "GUARD", false,
                    () => _hud.Order(TurfOrder.Holding, crew.Plan, null));
                Action(_dossierRect, rightX, y, half, "PATROL", false,
                    () => _hud.Order(TurfOrder.Walking, crew.Plan, null));
                y -= step;
                Action(_dossierRect, Pad, y, half, "TAIL", false,
                    () => _hud.Order(TurfOrder.Moving, crew.Plan, null));
                Action(_dossierRect, rightX, y, half, "FLEE", true,
                    () => _hud.Order(TurfOrder.PullingBack, crew.Plan, null));
                y -= step;

                // A mark AND its word. The mark alone was a scribble of rotated
                // rectangles floating in the middle of a hundred-unit button, which
                // read as neither a picture nor a label.
                Action(_dossierRect, Pad, y, half, "HOME", false,
                    () => _hud.Order(TurfOrder.ToTheOutfit, crew.Plan, null),
                    TurfGlyphs.House);
                Action(_dossierRect, rightX, y, half, "CAR", false,
                    () => _hud.Order(TurfOrder.InTheCar, crew.Plan, null),
                    TurfGlyphs.Car);
                y -= step;
            }
            else
            {
                Action(_dossierRect, Pad, y, inner, "WATCHED, NOT SPOKEN TO", false, null);
                y -= step;
            }

            y -= Pad - 4f;
            float tall = -y;
            _dossierRect.sizeDelta = new Vector2(0f, tall);

            LedgerKit.Rule(_dossierRect, 0f, -tall + 2f, width, Ink, 2f);
            return tall;
        }

        /// <summary>One of the dossier's three ledger skills. It deliberately uses the
        /// exact ten-mark half-step meter found on the personnel file, rather than a
        /// map-only star rating.</summary>
        float SkillRow(Transform parent, float x, float y, float w, string label, int halfSteps)
        {
            const float labelWide = 126f;
            float meter = LedgerKit.StepBarWidth(10, 5f, 7f);
            var row = DemoUi.NewRect("Stat", parent);
            LedgerKit.PlaceTopLeft(row, x, y, w, StatHeight);
            var text = LedgerKit.Line(row, LedgerStyle.Mono, 9f, Body, 0f,
                Mid(StatHeight, LedgerKit.LineBox(9f)), Mathf.Min(labelWide, w - meter - 8f),
                LedgerKit.LineBox(9f), label);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerKit.StepBar(row, w - meter, -StatHeight * 0.5f, 10,
                Mathf.Clamp(halfSteps, 0, 10), Red, 5f, 9f, 7f);

            return StatHeight;
        }

        /// <summary>
        /// Read-only organization manpower. Assignment and recruitment moved to the
        /// Ledger's ORGANIZATION dossier; the tactical map no longer edits the roster.
        /// </summary>
        float CrewCountRow(Transform parent, float x, float y, float w, TurfCrew crew)
        {
            const float inset = 6f;
            var row = DemoUi.NewRect("Hoods", parent);
            LedgerKit.PlaceTopLeft(row, x, y, w, StatHeight);
            LedgerKit.Fill(row, Well);
            LedgerKit.Frame(row, 1f, RuleFaint);
            Caps(row, inset, Mid(StatHeight, LedgerKit.LineBox(MicroType)), 72f, "MANPOWER",
                MicroType, Label, LedgerStyle.Condensed);

            var query = PersonnelDirector.Instance?.Organization;
            var hasCapacity = query != null && crew.Lieutenant != null;
            var capacity = hasCapacity
                ? query.CapacityOf(crew.Lieutenant.Id).Manpower
                : default;
            Caps(row, 82f, Mid(StatHeight, LedgerKit.LineBox(8f)), w - 150f,
                "MANAGE IN ORGANIZATION LEDGER", 8f, Dim, LedgerStyle.Condensed);
            LedgerKit.Line(row, LedgerStyle.Mono, 11f,
                capacity.IsOverCapacity ? Red : Body, w - 66f, 0f, 60f,
                StatHeight, hasCapacity
                    ? capacity.Current + " / " + capacity.Maximum
                    : "— / —",
                TextAlignmentOptions.MidlineRight);
            return StatHeight + 4f;
        }

        float KitBox(Transform parent, float x, float y, float w, string text)
        {
            const float h = 18f;
            var box = DemoUi.NewRect("Kit", parent);
            LedgerKit.PlaceTopLeft(box, x, y, w, h);
            LedgerKit.Frame(box, 1f, Rule);
            var line = LedgerKit.Line(box, LedgerStyle.Mono, 9f, Body, 6f,
                Mid(h, LedgerKit.LineBox(9f)), w - 12f, LedgerKit.LineBox(9f), text ?? "");
            line.overflowMode = TextOverflowModes.Ellipsis;
            return h + 3f;
        }

        /// <summary>
        /// One button on the file, optionally with a mark beside its word. The mark and
        /// the word are laid out as ONE block and that block is centred, so a button
        /// with a picture on it sits on the same line as the plain ones next to it.
        /// </summary>
        void Action(Transform parent, float x, float y, float w, string label,
            bool accent, UnityEngine.Events.UnityAction run, Sprite mark = null)
        {
            const float markW = 20f, markH = 14f, markToWord = 6f;

            var button = DemoUi.NewRect("Action", parent);
            LedgerKit.PlaceTopLeft(button, x, y, w, ButtonHeight);
            var face = Clickable(button, Well);
            LedgerKit.Frame(button, 1f, accent ? Red : Rule);

            var colour = accent ? (Color)Red : (Color)Slate;
            const float size = 10f, tracking = 16f;
            float words = Wide(label, size, LedgerStyle.Condensed, tracking);
            float block = mark != null ? markW + markToWord + words : words;
            float left = Mathf.Max(4f, (w - block) * 0.5f);

            Image picture = null;
            if (mark != null)
            {
                var glyph = DemoUi.NewRect("Mark", button);
                LedgerKit.PlaceTopLeft(glyph, left, Mid(ButtonHeight, markH), markW, markH);
                picture = glyph.gameObject.AddComponent<Image>();
                picture.sprite = mark;
                picture.color = colour;
                picture.raycastTarget = false;
                left += markW + markToWord;
            }

            var text = LedgerKit.Line(button, LedgerStyle.Condensed, size, colour,
                left, Mid(ButtonHeight, LedgerKit.LineBox(size)), words + 2f,
                LedgerKit.LineBox(size), label);
            text.characterSpacing = tracking;

            if (run != null)
            {
                LedgerKit.RowButton(button, face, run);
                Hover.Add(button, face, text, colour, new Color32(242, 230, 204, 255),
                    null, picture);
            }
        }

        // ----------------------------------------------------------------- header

        float BuildHeader(float width, float top)
        {
            const float h = 30f;
            const float buttonH = 17f;
            var header = DemoUi.NewRect("Header", _content);
            LedgerKit.PlaceTopLeft(header, 0f, top, width, h);
            LedgerKit.Fill(header, PlateSolid);
            LedgerKit.Rule(header, 0f, -h + 1f, width, Rule, 1f);

            bool inspecting = _hud.InspectedBuilding != null || _hud.InspectedDistrict != null;

            // ONE button on this row. The wash's switch used to sit here too and the two
            // of them together left the roster's own title less room than its words - it
            // read "CREWS AFIELD · LI…" for as long as it was up there. The switch now
            // lives on the turf key, which is where the wash is explained.
            float buttonY = Mid(h, buttonH);
            string back = inspecting ? "BACK TO CREWS" : "GATHER ALL";
            float backWide = ButtonWidth(back);
            float right = width - HeadPad - backWide;
            SmallButton(header, right, buttonY, backWide, buttonH, back, false,
                inspecting ? (UnityEngine.Events.UnityAction)_hud.ClearInspection
                           : _hud.GatherAll);

            // Centred on the same line the button is centred on, and given exactly the
            // room the button left it - and told what it may print in that room, rather
            // than handed a title too long for it and an ellipsis to cut it with.
            float box = Mathf.Max(20f, right - ButtonGap - HeadPad);
            string title = _hud.InspectedBuilding != null ? "PROPERTY FILE"
                : _hud.InspectedDistrict != null ? "DISTRICT FILE"
                : Fits("CREWS AFIELD · LINE PRINTER 03", box) ?? "CREWS AFIELD";

            var label = Caps(header, HeadPad, Mid(h, LedgerKit.LineBox(10f)), box, title,
                10f, Slate, LedgerStyle.Condensed);
            label.overflowMode = TextOverflowModes.Ellipsis;

            return h;
        }

        /// <summary>The words if they fit in the room, and nothing if they do not - so a
        /// caller can print a shorter form of its own rather than an ellipsis.</summary>
        string Fits(string words, float room) =>
            Wide(words, 10f, LedgerStyle.Condensed, 10f) <= room ? words : null;

        /// <summary>Room for a button's words: what the face actually sets, plus the
        /// frame and a breath either side of it.</summary>
        float ButtonWidth(string label) =>
            Wide(label, 9f, LedgerStyle.Condensed, 10f) + 18f;

        const float ButtonGap = 6f;

        void SmallButton(Transform parent, float x, float y, float w, float h, string label,
            bool accent, UnityEngine.Events.UnityAction run)
        {
            var colour = accent ? (Color)Red : (Color)new Color32(92, 77, 52, 255);
            var button = DemoUi.NewRect("Button", parent);
            LedgerKit.PlaceTopLeft(button, x, y, w, h);
            var face = Clickable(button, new Color(0f, 0f, 0f, 0f));
            LedgerKit.Frame(button, 1f, accent ? Red : Rule);
            var text = LedgerKit.Line(button, LedgerStyle.Condensed, 9f, colour,
                0f, 0f, w, h, label, TextAlignmentOptions.Center);
            text.characterSpacing = 10f;
            LedgerKit.RowButton(button, face, run);
            Hover.Add(button, face, text, colour, new Color32(242, 230, 204, 255));
        }

        // ----------------------------------------------------------------- roster

        /// <summary>The roster's columns are shared by heading and every row. The
        /// first column is the lieutenant's existing street camera, not a duplicate
        /// block of crew information.</summary>
        const float FeedColumn = 6f, FeedWide = 36f;
        const float NameColumn = FeedColumn + FeedWide + 8f, MenWide = 34f;

        float BuildRoster(float width, float top)
        {
            float y = top;
            const float headH = 20f;
            float box = LedgerKit.LineBox(9f);

            var head = DemoUi.NewRect("Columns", _content);
            LedgerKit.PlaceTopLeft(head, 0f, y, width, headH);
            LedgerKit.Rule(head, 0f, -headH + 1f, width, RuleFaint, 1f);
            Caps(head, FeedColumn, Mid(headH, box), FeedWide, "LIVE", 8f, Label,
                LedgerStyle.Condensed);
            Caps(head, NameColumn, Mid(headH, box), 120f, "LIEUTENANT", 9f, Label,
                LedgerStyle.Condensed);
            Caps(head, width - HeadPad - MenWide, Mid(headH, box), MenWide, "MEN", 9f, Label,
                LedgerStyle.Condensed, TextAlignmentOptions.MidlineRight);
            y -= headH;

            int men = 0;
            foreach (var crew in _hud.Units)
            {
                if (!crew.Mine || !crew.Alive)
                    continue;

                men += crew.MenStanding;
                y -= RosterRow(crew, width, y);
            }

            y -= 10f;
            LedgerKit.Rule(_content, HeadPad, y, width - HeadPad * 2f, Ink, 2f);
            y -= 8f;

            const float totalTall = 20f;
            Caps(_content, HeadPad, y + Mid(totalTall, LedgerKit.LineBox(10f)), 120f,
                "ON THE STREET", 10f, Ink, LedgerStyle.Condensed);
            LedgerKit.Line(_content, LedgerStyle.MonoBold, 14f, Ink,
                width - HeadPad - 90f, y + Mid(totalTall, LedgerKit.LineBox(14f)), 90f,
                LedgerKit.LineBox(14f), men + " MEN", TextAlignmentOptions.MidlineRight);
            y -= totalTall + 6f;

            return top - y;
        }

        /// <summary>
        /// One lieutenant. His current street view replaces the old ordinal number;
        /// name, men and order stay here because this is already their compact dossier.
        /// </summary>
        float RosterRow(TurfCrew crew, float width, float top)
        {
            bool on = _hud.IsGathered(crew.Id);

            var row = DemoUi.NewRect("Crew", _content);
            LedgerKit.PlaceTopLeft(row, 0f, top, width, RosterHeight);
            var face = Clickable(row,
                on ? new Color32(143, 33, 25, 23) : new Color(0f, 0f, 0f, 0f));
            LedgerKit.Rule(row, 0f, -RosterHeight + 1f, width, RuleFaint, 1f);

            if (on)
            {
                var edge = DemoUi.NewRect("Picked", row);
                LedgerKit.PlaceTopLeft(edge, 0f, 0f, 3f, RosterHeight);
                LedgerKit.Fill(edge, Red);
            }

            // Two lines through the row, and every reading placed by the line it belongs
            // on rather than by its own box. A thirteen-point name asks for a box two
            // thirds as tall again as its letters, so stacking the boxes put the
            // standing order underneath the NEXT lieutenant.
            const float nameLine = -13f, orderLine = -26f;
            float wide = width - HeadPad - MenWide - 6f - NameColumn;

            var live = DemoUi.NewRect("Live", row);
            LedgerKit.PlaceTopLeft(live, FeedColumn, -3f, FeedWide, FeedWide);
            LedgerKit.Fill(live, new Color32(30, 29, 25, 255));
            LedgerKit.Frame(live, 0.5f, Hairline);
            var feed = DemoUi.NewRect("Feed", live);
            DemoUi.Fill(feed);
            var picture = feed.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            picture.enabled = false;
            var stream = picture.gameObject.AddComponent<LieutenantLiveFeed>();
            stream.Unit = crew.Unit;

            var name = LedgerKit.Line(row, LedgerStyle.Mono, 13f, Ink, NameColumn,
                On(nameLine, LedgerKit.LineBox(13f)), wide, LedgerKit.LineBox(13f),
                crew.Name);
            name.overflowMode = TextOverflowModes.Ellipsis;

            LedgerKit.Line(row, LedgerStyle.Mono, 11f, new Color32(74, 63, 44, 255),
                width - HeadPad - MenWide, On(nameLine, LedgerKit.LineBox(11f)),
                MenWide, LedgerKit.LineBox(11f), crew.MenStanding.ToString(),
                TextAlignmentOptions.MidlineRight);

            var under = LedgerKit.Line(row, LedgerStyle.Mono, MicroType, Dim, NameColumn,
                On(orderLine, LedgerKit.LineBox(MicroType)),
                width - HeadPad - NameColumn, LedgerKit.LineBox(MicroType),
                crew.Gun + " · " + TurfOrders.Label(crew.Order));
            under.overflowMode = TextOverflowModes.Ellipsis;

            LedgerKit.RowButton(row, face, () => _hud.SelectOnly(crew));
            RightClick.Add(row, () => _hud.Focus(crew));
            return RosterHeight;
        }

        // ------------------------------------------------------------------ files

        float BuildPropertyFile(TurfBuilding building, float width, float top)
        {
            var house = TurfHouses.For(building.GangId);
            float metresPerUnit = _hud.Survey.Plan.MetresPerUnit;

            // Only what the city knows. Floors are derived from the height and say so;
            // the take is the business's own figure and the row is left off when it
            // has none, because a number invented for a file is a lie in a ledger.
            string text =
                building.Name + "\n" +
                building.District + "\n" +
                "HELD BY: " + (building.GangId < 0 ? "UNCLAIMED" : house.Name) + "\n" +
                "FOOTPRINT: " +
                    Mathf.RoundToInt(_hud.Survey.Plan.Units(building.World.width) * TurfPlate.S) +
                    " × " +
                    Mathf.RoundToInt(_hud.Survey.Plan.Units(building.World.height) * TurfPlate.S) +
                    " px (" + Mathf.RoundToInt(building.World.width) + " × " +
                    Mathf.RoundToInt(building.World.height) + " m)\n" +
                "FLOORS: ~" + building.Floors;
            if (building.Rent > 0)
                text += "\nTAKE: $" + building.Rent + " a week";

            // LEGACY / DEPRECATED (CTRL-011): this button keeps the existing demo path
            // reachable; new territory UI uses the stable-ID command/query boundary.
            return FileSheet(width, top, "PROPERTY FILE", text,
                "surveyed from the street · owner unaware",
                building.GangId == 0 ? "ALREADY OURS" : "TAKE IT",
                building.GangId == 0 ? null : (UnityEngine.Events.UnityAction)(() =>
                    _hud.Order(TurfOrder.Taking,
                        _hud.Survey.Plan.ToPlan(building.World.center), building)),
                metresPerUnit);
        }

        float BuildDistrictFile(TurfDistrict district, float width, float top)
        {
            int held = 0, ours = 0;
            foreach (var building in _hud.Survey.Buildings)
            {
                if (!district.World.Contains(building.World.center))
                    continue;
                held++;
                if (building.GangId == 0)
                    ours++;
            }

            string text =
                district.Name + "\n" +
                "HELD BY: " + (district.Contested ? "CONTESTED"
                    : district.GangId < 0 ? "UNCLAIMED" : district.House.Name) + "\n" +
                "FOOTPRINTS: " + held + "   OURS: " + ours + "\n" +
                "GROUND: " + (district.World.width / 1000f).ToString("0.0") + " × " +
                    (district.World.height / 1000f).ToString("0.0") + " km";

            return FileSheet(width, top, "DISTRICT FILE", text,
                "pencil marks are this month's · ink is last year's", null, null,
                _hud.Survey.Plan.MetresPerUnit);
        }

        float FileSheet(float width, float top, string number, string body, string foot,
            string action, UnityEngine.Events.UnityAction run, float metresPerUnit)
        {
            float y = top - 12f;
            float inner = width - HeadPad * 2f;

            Caps(_content, HeadPad, y, inner, number, 10f, Dim, LedgerStyle.Mono);
            y -= 18f;

            var text = LedgerKit.Paragraph(_content, LedgerStyle.Mono, 11f, Body,
                HeadPad, y, inner, 200f, body, 8f);
            text.ForceMeshUpdate();
            float used = text.preferredHeight;
            text.rectTransform.sizeDelta = new Vector2(inner, used);
            y -= used + 12f;

            if (action != null)
            {
                Action(_content, HeadPad, y,
                    Wide(action, 10f, LedgerStyle.Condensed, 16f) + 24f, action,
                    run != null, run);
                y -= ButtonHeight + 6f;
            }

            // Wrapped, not cut. At eleven points this line is half again wider than
            // the panel, and an ellipsis on the one piece of prose in the file reads
            // as a fault rather than as a margin note.
            var note = LedgerKit.Paragraph(_content, LedgerStyle.MonoItalic, 10f,
                new Color32(122, 104, 74, 255), HeadPad, y, inner, 40f, foot, 3f);
            note.ForceMeshUpdate();
            float noteTall = note.preferredHeight;
            note.rectTransform.sizeDelta = new Vector2(inner, noteTall);
            y -= noteTall + 12f;

            // the sheet reports the plate's own scale once, so a reader can turn a
            // pixel back into a street
            Caps(_content, HeadPad, y, inner,
                "1 PX ≈ " + (metresPerUnit / TurfPlate.S).ToString("0.0") + " M",
                MicroType, Label, LedgerStyle.Condensed);
            y -= 20f;

            return top - y;
        }

        // ------------------------------------------------------------------ menu

        internal void OpenEnemyMenu(Vector2 screen, DemoCrews.Unit actor,
            DemoCrews.Unit target, IReadOnlyList<CrewEnemyAction> actions)
        {
            if (actor == null || target == null || actions == null || actions.Count == 0)
            {
                CloseMenu();
                return;
            }

            TurfContextMenuStyle.ClearContent(_menuRect);
            _menuActor = actor;
            _menuTarget = target;

            const float headH = TurfContextMenuStyle.HeaderHeight;
            const float itemH = TurfContextMenuStyle.EnemyRowHeight;
            const float w = TurfContextMenuStyle.EnemyWidth;

            TurfContextMenuStyle.Header(_menuRect, w,
                target.GangName + " · " + target.Standing() + " MEN");

            float y = -headH;
            foreach (var action in actions)
            {
                var row = DemoUi.NewRect("Item", _menuRect);
                LedgerKit.PlaceTopLeft(row, 0f, y, w, itemH);
                var face = Clickable(row, TurfContextMenuStyle.Clear);
                TurfContextMenuStyle.EnemyText(row, w, out var label, out var note);
                label.text = action.Label;
                note.text = action.Note;

                var call = action.Run;
                if (call != null)
                {
                    LedgerKit.RowButton(row, face, () => { call(); CloseMenu(); });
                    Hover.Add(row, face, label, TurfContextMenuStyle.Body,
                        TurfContextMenuStyle.Accent, TurfContextMenuStyle.Hover);
                }
                else
                {
                    label.color = TurfContextMenuStyle.Disabled;
                    note.color = TurfContextMenuStyle.Disabled;
                }
                y -= itemH;
            }

            _menuRect.sizeDelta = new Vector2(w, -y + TurfContextMenuStyle.FooterHeight);

            // The cursor arrives in real pixels and the menu is measured in canvas
            // units; the reading has to come onto the canvas's own ladder before it
            // can be compared with a width. Then it is nudged back inside the window
            // if the click landed near an edge.
            float ui = _hud.UiScale;
            var at = screen / ui;
            float px = Mathf.Min(at.x, Screen.width / ui - w - 4f);
            float py = Mathf.Max(at.y, _menuRect.sizeDelta.y + 4f);
            Anchor(_menuRect, 0f, 0f, px, py);
            _menuRect.pivot = new Vector2(0f, 1f);
            _menuRect.gameObject.SetActive(true);
        }

        public void CloseMenu()
        {
            _menuActor = null;
            _menuTarget = null;
            if (_menuRect != null)
                _menuRect.gameObject.SetActive(false);
        }

        public bool MenuOpen => _menuRect != null && _menuRect.gameObject.activeSelf;

        /// <summary>Whether the pointer is on paper rather than on the map. The map's
        /// own picks are polled from the mouse, so they have to stand aside for the
        /// panel the way the street's picker stands aside for the map.</summary>
        public bool ClaimsPointer(Vector2 screen)
        {
            if (_menuRect != null && _menuRect.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(_menuRect, screen))
                return true;
            if (_panelRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screen))
                return true;
            return _keyRect != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(_keyRect, screen);
        }

        // ------------------------------------------------------------------ places

        /// <summary>
        /// The names over the ground. With the wash ON they are turf chips - who holds
        /// the quarter, in the family's own colour, on a scrap of paper set very
        /// slightly askew. With it OFF they are plain place names, because a map with
        /// no wash and no labels is a diagram.
        ///
        /// Built rarely, MOVED every frame. A chip labels a piece of city, so it has to
        /// stay over that city while the player pans; rebuilding is what is expensive
        /// and repositioning is two floats, and tying the two together left every place
        /// name standing where the ground used to be.
        /// </summary>
        void Places()
        {
            if (_placesRoot.childCount > 0 && _placesTurf == _hud.TurfOn &&
                _placesStamp == _paintedStamp)
            {
                PlaceChips();
                return;
            }

            _placesTurf = _hud.TurfOn;
            _placesStamp = _paintedStamp;
            _chips.Clear();
            for (int i = _placesRoot.childCount - 1; i >= 0; i--)
                Destroy(_placesRoot.GetChild(i).gameObject);

            int index = 0;
            foreach (var district in _hud.Survey.Districts)
            {
                index++;
                if (!_hud.TurfOn && district.GangId != -1 && index % 2 == 0)
                    continue;

                var chip = DemoUi.NewRect("Place", _placesRoot);
                chip.anchorMin = chip.anchorMax = new Vector2(0f, 0f);
                chip.pivot = new Vector2(0.5f, 0.5f);
                _chips.Add((chip, district.World.center));

                if (_hud.TurfOn)
                {
                    var house = district.House;
                    string label = district.Contested ? "CONTESTED · " + district.Name
                        : district.GangId < 0 ? district.Name
                        : house.Short + " · " + district.Name;

                    float wide = Wide(label, 11f, LedgerStyle.Condensed, 14f) + 14f;
                    chip.sizeDelta = new Vector2(wide, 18f);
                    LedgerKit.Fill(chip, new Color32(247, 240, 218, 204));
                    LedgerKit.Frame(chip, 1f, house.Ink);
                    var text = LedgerKit.Line(chip, LedgerStyle.Condensed, 11f, house.Ink,
                        0f, 0f, wide, 18f, label, TextAlignmentOptions.Center);
                    text.characterSpacing = 14f;
                    chip.localRotation = Quaternion.Euler(0f, 0f, index % 2 == 0 ? 0.9f : -1.1f);
                }
                else
                {
                    float wide = Wide(district.Name, 15f, LedgerStyle.Condensed, 20f) + 20f;
                    chip.sizeDelta = new Vector2(wide, 24f);
                    var text = LedgerKit.Line(chip, LedgerStyle.Condensed, 15f, Ink,
                        0f, 0f, wide, 24f, district.Name, TextAlignmentOptions.Center);
                    text.characterSpacing = 20f;
                }
            }

            PlaceChips();
        }

        /// <summary>Each chip over the ground it names, and hidden rather than left
        /// hanging in the margin when that ground has panned off the sheet.</summary>
        void PlaceChips()
        {
            float ui = _hud.UiScale;
            foreach (var (chip, world) in _chips)
            {
                if (chip == null)
                    continue;

                var screen = _hud.WorldToScreen(world);
                bool onSheet = screen.x >= 40f && screen.y >= 40f &&
                               screen.x <= Screen.width - 40f &&
                               screen.y <= Screen.height - 40f;

                chip.gameObject.SetActive(onSheet);
                if (onSheet)
                    // The reading is in screen pixels; this canvas is not. On a 4K
                    // window a raw screen point lands at half the distance out from
                    // the corner.
                    chip.anchoredPosition = screen / ui;
            }
        }

        /// <summary>Every place chip and the world point it labels.</summary>
        readonly List<(RectTransform Chip, Vector2 World)> _chips =
            new List<(RectTransform, Vector2)>();

        bool _placesTurf;
        int _placesStamp = -1;

        // ------------------------------------------------------------------ hover

        /// <summary>
        /// The hover state the design puts on every clickable line - a wash of oxblood
        /// under it and the marks over it in the paper's own colour. uGUI's own
        /// Selectable transitions tint ONE graphic; a button here has a face, a word and
        /// sometimes a picture beside the word, and a picture left dark on an oxblood
        /// wash is the one thing on the row that did not hear the pointer arrive.
        /// </summary>
        sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            Graphic _face, _label, _mark;
            Color _faceRest, _faceOver, _inkRest, _inkOver;

            public static void Add(RectTransform rect, Graphic background, Graphic text,
                Color textRest, Color textOver, Color? backgroundOver = null,
                Graphic mark = null)
            {
                var hover = rect.gameObject.AddComponent<Hover>();
                hover._face = background;
                hover._label = text;
                hover._mark = mark;
                hover._faceRest = background != null ? background.color : Color.clear;
                hover._faceOver = backgroundOver ?? (Color)Red;
                hover._inkRest = textRest;
                hover._inkOver = textOver;
            }

            public void OnPointerEnter(PointerEventData eventData) => Paint(true);

            public void OnPointerExit(PointerEventData eventData) => Paint(false);

            void Paint(bool over)
            {
                if (_face != null)
                    _face.color = over ? _faceOver : _faceRest;
                if (_label != null)
                    _label.color = over ? _inkOver : _inkRest;
                if (_mark != null)
                    _mark.color = over ? _inkOver : _inkRest;
            }
        }

        /// <summary>A Button deliberately owns only the row's left click. This small
        /// companion gives the right button its camera verb without changing selection.</summary>
        sealed class RightClick : MonoBehaviour, IPointerClickHandler
        {
            System.Action _run;

            public static void Add(RectTransform rect, System.Action run)
            {
                var click = rect.gameObject.AddComponent<RightClick>();
                click._run = run;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Right)
                    return;
                _run?.Invoke();
                eventData.Use();
            }
        }
    }

    /// <summary>
    /// The two marks the file's last row carries - a house to send a crew back to the
    /// outfit, a car to put it back in one. There is no icon font in this project and
    /// no sprite for either.
    ///
    /// They are BAKED, as pixels, rather than assembled out of rotated rectangles:
    /// rotated rects were what was here, and a roof made of two thin bars pivoted about
    /// their own left ends comes out as a scribble at fourteen units tall. A tiny
    /// point-filtered raster is also the right register for this screen - the map under
    /// it is a raster plate.
    ///
    /// White pixels, tinted at the Image. That is what lets a button's mark go pale
    /// with its word when the pointer arrives, off one sprite rather than two.
    /// </summary>
    static class TurfGlyphs
    {
        const int Wide = 20, Tall = 14;

        /// <summary>
        /// Baked once and kept. There is deliberately no reset at play: these two are
        /// constant pixels and nothing about a new session can make them wrong, so a
        /// pair that survived a domain reload is a pair worth keeping. What the reset
        /// WOULD do is drop the reference and leave the native texture behind.
        ///
        /// The tests below are Unity's null, not C#'s, so a sprite the editor did unload
        /// between sessions is baked again rather than handed on as a dead reference.
        /// </summary>
        static Sprite _house, _car;

        public static Sprite House
        {
            get
            {
                if (_house == null)
                    _house = Bake("Turf Glyph House", PaintHouse);
                return _house;
            }
        }

        public static Sprite Car
        {
            get
            {
                if (_car == null)
                    _car = Bake("Turf Glyph Car", PaintCar);
                return _car;
            }
        }

        static Sprite Bake(string name, System.Action<Color32[]> paint)
        {
            var pixels = new Color32[Wide * Tall];
            paint(pixels);

            var texture = new Texture2D(Wide, Tall, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0f, 0f, Wide, Tall),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        /// <summary>Rows are given TOP DOWN, the way the shape reads on paper; the
        /// texture is bottom-up, so the row index is turned over here and nowhere
        /// else.</summary>
        static void Row(Color32[] pixels, int fromTop, int x0, int x1)
        {
            int y = Tall - 1 - fromTop;
            if (y < 0 || y >= Tall)
                return;
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Wide - 1, x1); x++)
                pixels[y * Wide + x] = new Color32(255, 255, 255, 255);
        }

        static void Clear(Color32[] pixels, int fromTop, int x0, int x1)
        {
            int y = Tall - 1 - fromTop;
            if (y < 0 || y >= Tall)
                return;
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Wide - 1, x1); x++)
                pixels[y * Wide + x] = new Color32(0, 0, 0, 0);
        }

        /// <summary>A gable roof over a body, with a door knocked out of it.</summary>
        static void PaintHouse(Color32[] pixels)
        {
            for (int r = 0; r < 6; r++)
                Row(pixels, r, 9 - r * 2, 10 + r * 2);   // the roof, widening by two a row

            for (int r = 6; r < Tall; r++)
                Row(pixels, r, 3, 16);                   // the body, under the eaves

            for (int r = 9; r < Tall; r++)
                Clear(pixels, r, 8, 11);                 // the doorway
            for (int r = 7; r <= 8; r++)
            {
                Clear(pixels, r, 5, 6);                  // two windows
                Clear(pixels, r, 13, 14);
            }
        }

        /// <summary>A saloon in profile: cabin, body, two wheels under it. Rows 2 to 11
        /// of fourteen, so the shape sits on the same middle line the house does.
        /// </summary>
        static void PaintCar(Color32[] pixels)
        {
            Row(pixels, 2, 6, 13);                       // the cabin
            Row(pixels, 3, 5, 14);
            Row(pixels, 4, 4, 15);
            for (int r = 5; r <= 8; r++)
                Row(pixels, r, 1, 18);                   // the body

            Clear(pixels, 3, 6, 8);                      // the glass
            Clear(pixels, 3, 11, 13);
            Clear(pixels, 4, 5, 8);
            Clear(pixels, 4, 11, 14);

            for (int r = 9; r <= 11; r++)
            {
                Row(pixels, r, 3, 6);                    // the wheels
                Row(pixels, r, 13, 16);
            }
            Clear(pixels, 10, 4, 5);
            Clear(pixels, 10, 14, 15);
        }
    }
}
