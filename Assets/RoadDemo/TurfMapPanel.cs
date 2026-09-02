using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.Territory;
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

        static readonly Color Hairline = new Color32(43, 36, 24, 140);
        static readonly Color Rule = new Color32(43, 36, 24, 90);
        static readonly Color RuleFaint = new Color32(43, 36, 24, 40);
        static readonly Color Ink = new Color32(43, 36, 24, 255);
        static readonly Color Body = new Color32(47, 40, 32, 255);
        static readonly Color Red = new Color32(143, 33, 25, 255);
        static readonly Color Label = new Color32(109, 92, 64, 255);
        static readonly Color Slate = new Color32(59, 50, 38, 255);

        TurfMapHud _hud;
        Canvas _canvas;
        bool _showMapChrome;

        RectTransform _keyRect, _placesRoot, _menuRect;
        DemoCrews.Unit _menuActor, _menuTarget;

        public void Init(TurfMapHud owner, bool showMapChrome)
        {
            _hud = owner;
            _canvas = GetComponent<Canvas>();
            _showMapChrome = showMapChrome;
            Build();
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            var root = (RectTransform)transform;
            BuildRuler(root);

            if (_showMapChrome)
                BuildMapChrome(root);
        }

        void BuildMapChrome(RectTransform root)
        {
            // Straight above the sheet, where it is created: the chrome that may cover
            // a place name - the key, the menu, the tip - is built after this and lands
            // over it on its own. Sent to the FRONT of the canvas, as it used to be,
            // these chips sat under the map's own backdrop and were never once seen.
            _placesRoot = DemoUi.NewRect("Places", root);
            DemoUi.Fill(_placesRoot);

            BuildKey(root);
            _paintedKey = KeyStamp();

            _menuRect = DemoUi.NewRect("Menu", root);
            TurfContextMenuStyle.Dress(_menuRect);
            _menuRect.gameObject.SetActive(false);

            // The hover tip: the same paper as the menu, six lines of words and no
            // controls at all - it answers "what is this street" while the pointer
            // passes over it, and disappears the moment it does not.
            _tipRect = DemoUi.NewRect("Block Tip", root);
            TurfContextMenuStyle.Dress(_tipRect);
            _tipText = LedgerKit.Paragraph(_tipRect, LedgerStyle.Mono, 11f,
                TurfContextMenuStyle.Body, TipPad, -TipPad, TipWidth - TipPad * 2f,
                TipHeight - TipPad * 2f, "", 6f);
            _tipText.raycastTarget = false;
            _tipRect.gameObject.SetActive(false);

            BuildPrecinct(root);
        }

        // -------------------------------------------------------------- the precinct

        RectTransform _precinctRect;
        TextMeshProUGUI _precinctLine;
        string _precinctShown = "";

        /// <summary>
        /// HOW MUCH LAW THIS END OF TOWN HAS (GAN-226, ROSTER-006). A station's strength
        /// used to be invisible: a player who had shot the precinct's crews to pieces
        /// found out by noticing that nobody came any more, which is indistinguishable
        /// from a bug. So the map says it - cars, men on duty, what is missing and the
        /// day it is back - and says NO LAW outright when the house is empty.
        ///
        /// Same paper and the same ink as the key strip opposite it; no raycaster, and
        /// the words are pushed at the label only when they have actually changed.
        /// </summary>
        void BuildPrecinct(RectTransform root)
        {
            _precinctRect = DemoUi.NewRect("Precinct", root);
            _precinctRect.anchorMin = _precinctRect.anchorMax = new Vector2(0f, 0f);
            _precinctRect.pivot = new Vector2(0f, 0f);
            _precinctRect.anchoredPosition = Vector2.zero;
            _precinctRect.sizeDelta = new Vector2(300f, 24f);
            LedgerKit.Fill(_precinctRect, new Color32(247, 240, 218, 230));
            LedgerKit.Rule(_precinctRect, 0f, 0f, 300f, Hairline, 1f);

            _precinctLine = LedgerKit.Line(_precinctRect, LedgerStyle.Condensed, 9f, Slate,
                10f, Mid(24f, LedgerKit.LineBox(9f)), 280f, LedgerKit.LineBox(9f), "");
            _precinctLine.characterSpacing = 10f;
            _precinctLine.raycastTarget = false;
            _precinctRect.gameObject.SetActive(false);
        }

        void PaintPrecinct()
        {
            if (_precinctLine == null) return;
            var force = _hud != null ? _hud.Force : null;
            var station = force != null ? force.Station : null;
            var line = station != null ? station.Roster.Plaque().ToUpperInvariant() : "";
            if (line == _precinctShown) return;
            _precinctShown = line;
            _precinctLine.SetText(line);
            _precinctRect.gameObject.SetActive(line.Length > 0);
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

            // The parcel lines, on the same strip and the same switch shape: the survey
            // is the picture of the city and the blocks are a reading laid over it, so
            // the player asks for them the way he asks for the ownership wash.
            string blockLabel = _hud.BlocksOn ? "BLOCKS ON" : "BLOCKS OFF";
            float blockWide = Wide(blockLabel, 9f, LedgerStyle.Condensed, 10f) + 18f;
            SmallButton(_keyRect, x, Mid(tall, switchH), blockWide, switchH, blockLabel,
                _hud.BlocksOn, () => _hud.SetBlocks(!_hud.BlocksOn));
            x += blockWide + 9f;

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
            int stamp = (_hud.TurfOn ? 1 : 0) * 31 + (_hud.BlocksOn ? 1 : 0);
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

        /// <summary>The pick moved. The map's key is what changes with it - which
        /// houses are worth a swatch - so the key is dropped and drawn again on the
        /// next refresh.</summary>
        public void SelectionChanged() => _paintedKey = -1;

        public void Refresh()
        {
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
                if (_door.Stale)
                    _door.Paint();
                PaintPrecinct();
                Places();
            }
        }

        /// <summary>What the panel is showing. Changing it repaints; a frame that
        /// changed nothing costs one integer.</summary>
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

        // ------------------------------------------------------------------ menu

        internal void OpenEnemyMenu(Vector2 screen, DemoCrews.Unit actor,
            DemoCrews.Unit target, IReadOnlyList<CrewEnemyAction> actions)
        {
            if (target == null)
            {
                CloseMenu();
                return;
            }

            OpenActionMenu(screen, actor, target,
                target.GangName + " · " + target.Standing() + " MEN", actions);
        }

        /// <summary>
        /// The same paper menu over anything the map can name - a rival's crew, or a
        /// shopkeeper's premises. The rows and what they do belong to whoever asked for
        /// them; this only draws them, so the street and the map can never offer
        /// different things.
        /// </summary>
        internal void OpenActionMenu(Vector2 screen, DemoCrews.Unit actor,
            DemoCrews.Unit target, string title, IReadOnlyList<CrewEnemyAction> actions)
        {
            if (actor == null || actions == null || actions.Count == 0)
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

            TurfContextMenuStyle.Header(_menuRect, w, title);

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

        const float TipWidth = 240f;
        const float TipHeight = 118f;
        const float TipPad = 8f;

        RectTransform _tipRect;
        TMP_Text _tipText;

        /// <summary>
        /// What the street under the pointer is, in the player's words. Six short lines,
        /// no numbers, and nothing to click - a tip is a look, never an order.
        /// </summary>
        public void ShowBlockTip(string text, Vector2 screen)
        {
            if (_tipRect == null || string.IsNullOrEmpty(text))
            {
                HideBlockTip();
                return;
            }

            _tipText.text = text;
            _tipRect.sizeDelta = new Vector2(TipWidth, TipHeight);

            // Same ladder as the menu: real pixels onto canvas units, then nudged back
            // inside the window so a tip near an edge is still readable.
            float ui = _hud.UiScale;
            var at = screen / ui;
            float px = Mathf.Min(at.x + 18f, Screen.width / ui - TipWidth - 4f);
            float py = Mathf.Max(at.y - 12f, TipHeight + 4f);
            Anchor(_tipRect, 0f, 0f, px, py);
            _tipRect.pivot = new Vector2(0f, 1f);
            if (!_tipRect.gameObject.activeSelf)
                _tipRect.gameObject.SetActive(true);
        }

        public void HideBlockTip()
        {
            if (_tipRect != null && _tipRect.gameObject.activeSelf)
                _tipRect.gameObject.SetActive(false);
        }

        public void CloseMenu()
        {
            _menuActor = null;
            _menuTarget = null;
            if (_menuRect != null)
                _menuRect.gameObject.SetActive(false);
        }

        public bool MenuOpen => _menuRect != null && _menuRect.gameObject.activeSelf;

        // ------------------------------------------------------------ the door's menu

        /// <summary>The shop's own menu, floating over the plate. Not a menu of the map's
        /// - it is <see cref="DoorMenu"/>, the same panel the ledger opens beside a row of
        /// its block file and the street opens over a facade, with the same men picked and
        /// the same keys.</summary>
        readonly DoorMenu.Host _door = new DoorMenu.Host();

        public bool DoorMenuOpen => _door.IsOpen;

        public bool OpenDoorMenu(Vector2 screen, TerritoryBusinessId id) =>
            _door.Show(id, screen);

        public void CloseDoorMenu() => _door.Close();

        /// <summary>Whether the pointer is on the map's own chrome rather than on the
        /// map. The map's picks are polled from the mouse, so they have to stand aside
        /// for the order menu and the key the way the street's picker stands aside for
        /// the map.</summary>
        public bool ClaimsPointer(Vector2 screen)
        {
            if (_menuRect != null && _menuRect.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(_menuRect, screen))
                return true;
            if (_door.Contains(screen))
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
        ///
        /// These are the survey's OWN districts, and they are printed only where the
        /// city has no territory plan. A city that has one is named by TurfMapHud off
        /// the shared rig - the block chips and quarter plates the street's O overlay
        /// prints - and the district chips would be those same neighbourhood names a
        /// second time, in a second style, half a chip away from the first.
        /// </summary>
        void Places()
        {
            if (TerritoryPlaques.Available(_hud.City))
                return;

            if (_placesRoot.childCount > 0 && _placesTurf == _hud.TurfOn &&
                _placesStamp == _paintedKey)
            {
                PlaceChips();
                return;
            }

            _placesTurf = _hud.TurfOn;
            _placesStamp = _paintedKey;
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

            /// <summary>The rest colours were taken when the control was built, which is
            /// a DAY reading; painted back raw they would put day ink on a night sheet
            /// the moment a pointer left a button after dark. Everything a hover sets
            /// therefore goes through the shared table on its way to the graphic - a
            /// colour the table does not know comes back untouched.</summary>
            void Paint(bool over)
            {
                if (_face != null)
                    _face.color = HudNight.Cross(over ? _faceOver : _faceRest);
                if (_label != null)
                    _label.color = HudNight.Cross(over ? _inkOver : _inkRest);
                if (_mark != null)
                    _mark.color = HudNight.Cross(over ? _inkOver : _inkRest);
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
