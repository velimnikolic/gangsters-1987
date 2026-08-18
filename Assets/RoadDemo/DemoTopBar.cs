using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The strip across the very top of the screen: the hour and the day on the left,
    /// the transport controls on the right - slower, the speed readout, faster, and
    /// hold. Reads DemoClock and nothing else.
    ///
    /// Dressed from DemoUi, which is the same Modern Menus pack the ledger wears, so
    /// the bar and the book the demo installs read as one piece of software. Two
    /// species of surface and no third: a KEY is a pack chip you can press (it tints
    /// under the pointer), a WELL is the same chip sunk and darkened for a figure you
    /// can only read. Colour says state and nothing else - ice for data, steel for
    /// labels, powder blue for the live accent, gold for a clock being held.
    ///
    /// The book takes the left half of the screen when it opens, so the bar gives it
    /// up and retracts to the right half rather than stacking two mastheads on top of
    /// each other. That is the one thing here that knows the ledger exists, and it
    /// knows it through a static bool.
    /// </summary>
    public class DemoTopBar : MonoBehaviour
    {
        public DemoClock clock;

        const float BarHeight = 52f;
        const float Edge = 26f;      // air between the bar's ends and its content
        const float KeyHeight = 34f;
        const float KeySize = 36f;   // a square icon key
        const float WellWidth = 78f;
        const float HoldWidth = 48f;
        const float Gap = 5f;        // inside the transport group
        const float GroupGap = 12f;  // between the group and the hold key

        TMP_Text _timeLabel;
        TMP_Text _dayLabel;
        TMP_Text _speedLabel;
        Image _holdGlyph;            // the play triangle, shown only while held
        RectTransform _pauseGlyph;   // the two bars, shown only while running
        RectTransform _barRect;

        int _shownMinute = -1;
        int _shownDay = -1;
        bool _retracted;

        void Start()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            gameObject.AddComponent<GraphicRaycaster>();

            // the demo scene has no UI until now, so no EventSystem either; the
            // legacy StandaloneInputModule throws under InputSystem-only projects
            if (!EventSystem.current)
            {
                var host = new GameObject("EventSystem");
                host.AddComponent<EventSystem>();
                host.AddComponent<InputSystemUIInputModule>();
            }

            BuildBar();
            Refresh();
        }

        void BuildBar()
        {
            _barRect = DemoUi.NewRect("Bar", transform);
            _barRect.anchorMin = new Vector2(0f, 1f);
            _barRect.anchorMax = new Vector2(1f, 1f);
            _barRect.pivot = new Vector2(0.5f, 1f);
            _barRect.anchoredPosition = Vector2.zero;
            _barRect.sizeDelta = new Vector2(0f, BarHeight);

            // The strip's floor is a flat slab, edge to edge. A bar glued to the top
            // of the screen wants a clean full bleed, and the pack's decorative menu
            // slab cannot give one - see DemoUi.Gradient for why.
            var floor = _barRect.gameObject.AddComponent<Image>();
            floor.color = DemoUi.BarFace;
            floor.raycastTarget = false;

            // Depth without a seam: the pack's vertical ramp in the accent colour,
            // clear at the screen edge and gathering toward the rule below, so the
            // bar looks lit from its own bottom edge rather than pasted on.
            var glow = DemoUi.Block(_barRect, "Glow",
                new Color(DemoUi.Accent.r, DemoUi.Accent.g, DemoUi.Accent.b, 0.10f));
            glow.sprite = DemoUi.Gradient;
            DemoUi.Fill(glow.rectTransform);

            // The accent rule along the bottom edge - where the chrome ends and the
            // world begins, said once, in the colour that means "live".
            var rule = DemoUi.Block(_barRect, "Rule",
                new Color(DemoUi.Accent.r, DemoUi.Accent.g, DemoUi.Accent.b, 0.5f));
            var ruleRect = rule.rectTransform;
            ruleRect.anchorMin = new Vector2(0f, 0f);
            ruleRect.anchorMax = new Vector2(1f, 0f);
            ruleRect.pivot = new Vector2(0.5f, 0f);
            ruleRect.anchoredPosition = Vector2.zero;
            ruleRect.sizeDelta = new Vector2(0f, 2f);

            BuildClockBlock();
            BuildTransport();
        }

        // ---- the hour, the day: the left end of the bar -----------------------

        void BuildClockBlock()
        {
            var timer = DemoUi.Icon(_barRect, "TimerIcon", DemoUi.IconTimer, 21f,
                DemoUi.Accent);
            PlaceLeft(timer.rectTransform, Edge, 21f, 21f);

            _timeLabel = DemoUi.Text(_barRect, "Time", 27f, DemoUi.Ink,
                TextAlignmentOptions.MidlineLeft, display: true);
            _timeLabel.characterSpacing = 3f;
            _timeLabel.text = "00:00";
            PlaceLeft(_timeLabel.rectTransform, Edge + 31f, 112f, 34f);

            var divider = DemoUi.Block(_barRect, "Divider",
                new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.35f));
            PlaceLeft(divider.rectTransform, Edge + 150f, 1f, 22f);

            _dayLabel = DemoUi.Text(_barRect, "Day", 15f, DemoUi.InkDim,
                TextAlignmentOptions.MidlineLeft);
            _dayLabel.characterSpacing = 6f;
            _dayLabel.text = "DAY 1";
            PlaceLeft(_dayLabel.rectTransform, Edge + 166f, 150f, 22f);
        }

        // ---- slower, the speed, faster, hold: the right end -------------------

        void BuildTransport()
        {
            // Laid right to left off the bar's own edge, so the group stays put on
            // any screen and the retract to the half-width bar costs nothing.
            var cursor = Edge;

            BuildHold(cursor);
            cursor += HoldWidth + GroupGap;

            BuildStep(cursor, "Faster", faster: true);
            cursor += KeySize + Gap;

            BuildSpeedWell(cursor);
            cursor += WellWidth + Gap;

            BuildStep(cursor, "Slower", faster: false);
        }

        void BuildStep(float fromRight, string name, bool faster)
        {
            var key = NewKey(name, fromRight, KeySize, KeyHeight, () => Step(faster));

            // One fast-forward glyph serves both steps: mirrored on X it IS the
            // rewind the pack never shipped, and the pair stays perfectly matched.
            var icon = DemoUi.Icon(key, "Glyph", DemoUi.IconFaster, 17f, DemoUi.Ink);
            if (!faster)
                icon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        }

        void BuildSpeedWell(float fromRight)
        {
            var well = DemoUi.NewRect("SpeedWell", _barRect);
            PlaceRight(well, fromRight, WellWidth, KeyHeight);
            var face = well.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            DemoUi.Dress(face, DemoUi.Chip, 13f, DemoUi.Well);

            _speedLabel = DemoUi.Text(well, "Value", 15f, DemoUi.Ink,
                TextAlignmentOptions.Center);
            DemoUi.Fill(_speedLabel.rectTransform);
            _speedLabel.characterSpacing = 2f;
            _speedLabel.fontStyle = FontStyles.Bold;
            _speedLabel.text = "1x";
        }

        void BuildHold(float fromRight)
        {
            var key = NewKey("Hold", fromRight, HoldWidth, KeyHeight, TogglePause);

            // Both glyphs are built now and one is hidden: a TextMeshPro-free pair of
            // Images costs nothing to keep, and swapping active beats rebuilding.
            _pauseGlyph = DemoUi.NewRect("PauseGlyph", key);
            DemoUi.Fill(_pauseGlyph);
            HoldBar(_pauseGlyph, -4.5f);
            HoldBar(_pauseGlyph, 4.5f);

            _holdGlyph = DemoUi.Icon(key, "PlayGlyph", DemoUi.IconPlay, 17f, DemoUi.Gold);
            _holdGlyph.gameObject.SetActive(false);
        }

        static void HoldBar(Transform parent, float offsetX)
        {
            var block = DemoUi.Block(parent, "Bar", DemoUi.Ink);
            block.rectTransform.sizeDelta = new Vector2(4f, 15f);
            block.rectTransform.anchoredPosition = new Vector2(offsetX, 0f);
        }

        /// <summary>A pressable pack chip: the face is the raycast target and the
        /// Button's tint surface, and every key in the demo answers the pointer
        /// through DemoUi.TintStates so none of them invents its own hover.</summary>
        RectTransform NewKey(string name, float fromRight, float width, float height,
            UnityEngine.Events.UnityAction onClick)
        {
            var rect = DemoUi.NewRect("Key " + name, _barRect);
            PlaceRight(rect, fromRight, width, height);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = true;
            DemoUi.Dress(face, DemoUi.Chip, 13f, DemoUi.KeyFace);

            var button = rect.gameObject.AddComponent<Button>();
            DemoUi.TintStates(button, face);
            button.onClick.AddListener(onClick);
            return rect;
        }

        // ------------------------------------------------------------------ state

        void TogglePause()
        {
            if (clock)
                clock.Paused = !clock.Paused;
            // The two halves of the same switch, and the one the player hears on the
            // frame the world goes quiet - DemoAudio keeps interface clicks out of
            // the pause fade for exactly this.
            DemoAudio.Ui(clock && clock.Paused ? DemoSounds.UiToggleOff : DemoSounds.UiToggleOn);
            Refresh();
        }

        /// <summary>One rung up or down the speed ladder. The key and the chip both
        /// come through here, so they cannot drift apart.</summary>
        void Step(bool faster)
        {
            if (!clock)
                return;
            if (faster)
                clock.SpeedUp();
            else
                clock.SlowDown();
            DemoAudio.Ui(DemoSounds.UiClick);
            Refresh();
        }

        void Refresh()
        {
            var paused = clock && clock.Paused;

            if (_pauseGlyph)
                _pauseGlyph.gameObject.SetActive(!paused);
            if (_holdGlyph)
                _holdGlyph.gameObject.SetActive(paused);

            // A held clock is the one state that demands attention, so it is the one
            // place gold appears on this bar.
            if (_speedLabel && clock)
            {
                _speedLabel.text = paused ? "PAUSED" : clock.SpeedMultiplier + "x";
                _speedLabel.color = paused ? DemoUi.Gold : DemoUi.Ink;
            }
        }

        void Update()
        {
            // The book owns the keyboard while it is open - it reads P, Esc, the
            // brackets and F2, and a stray space must not reach past it.
            var book = LivingCity.UI.PersonnelAlmanac.IsOpen;

            if (book != _retracted && _barRect)
            {
                _retracted = book;
                // The ledger's page is the LEFT half of the screen; the bar keeps the
                // right half rather than printing a second masthead over the first.
                _barRect.anchorMin = new Vector2(book ? 0.5f : 0f, 1f);
            }

            if (book)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !clock)
                return;

            if (keyboard.spaceKey.wasPressedThisFrame)
                TogglePause();
            // The comma and period keys carry < and > - the arrows the two step keys
            // draw, on the keys that already print them.
            if (keyboard.commaKey.wasPressedThisFrame)
                Step(faster: false);
            if (keyboard.periodKey.wasPressedThisFrame)
                Step(faster: true);
        }

        void LateUpdate()
        {
            if (!clock)
                return;

            var minute = Mathf.FloorToInt(clock.Hour * 60f);
            if (minute == _shownMinute)
                return;
            _shownMinute = minute;

            if (_timeLabel)
                _timeLabel.SetText("{0:00}:{1:00}", minute / 60, minute % 60);

            if (_dayLabel && clock.Day != _shownDay)
            {
                _shownDay = clock.Day;
                _dayLabel.text = "DAY " + (_shownDay + 1);
            }
        }

        // ------------------------------------------------------------- placement

        /// <summary>Anchored to the bar's left end, centred on its height.</summary>
        static void PlaceLeft(RectTransform rect, float x, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Anchored to the bar's right end, x measured inward.</summary>
        static void PlaceRight(RectTransform rect, float x, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-x, 0f);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
