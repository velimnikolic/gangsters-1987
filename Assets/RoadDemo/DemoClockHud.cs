using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.UI;

namespace RoadDemo
{
    /// <summary>
    /// The city's permanent time strip. It is a view of <see cref="DemoClock"/>, not
    /// a map control: the same date, pause and speed ladder remain available while
    /// the player is on the street, looking at the turf plan, or using the ledger.
    /// </summary>
    public sealed class DemoClockHud : MonoBehaviour
    {
        public const float Height = 48f;

        const float WidthFraction = 0.18f;
        const float DateTall = 24f;
        const float Pad = 11f;

        static readonly Color Paper = new Color32(244, 236, 214, 250);
        static readonly Color Ink = new Color32(43, 36, 24, 255);
        static readonly Color Rule = new Color32(43, 36, 24, 140);
        static readonly Color Red = new Color32(143, 33, 25, 255);
        static readonly Color Key = new Color32(43, 36, 24, 16);
        static readonly Color KeyOn = new Color32(143, 33, 25, 35);

        DemoClock _clock;
        Canvas _canvas;
        RectTransform _plate, _dateRect, _timeRect, _slower, _speed, _faster, _pause;
        TMP_Text _dateText, _timeText, _speedText, _pauseText;
        Image _pauseFace;
        int _shownMinute = -1, _shownDay = -1;

        DemoCamera _rig;
        RectTransform _timeScrubber;
        Slider _timeSlider;
        TMP_Text _scrubTime;
        int _scrubMinute = -1;

        public void Init(DemoClock clock)
        {
            _clock = clock;
            _rig = FindAnyObjectByType<DemoCamera>();
            Build();
        }

        float Width => Screen.width / Mathf.Max(0.01f, _canvas.scaleFactor) * WidthFraction;

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // The turf map is 100 and the ledger is 110. This HUD is deliberately
            // above both: time remains readable and controllable in every view.
            _canvas.sortingOrder = 120;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();

            if (!EventSystem.current)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>();
            }

            _plate = DemoUi.NewRect("Clock", transform);
            _plate.anchorMin = _plate.anchorMax = new Vector2(0f, 1f);
            _plate.pivot = new Vector2(0f, 1f);
            _plate.anchoredPosition = Vector2.zero;
            LedgerKit.Fill(_plate, Paper);
            LedgerKit.Frame(_plate, 0.5f, Rule);

            _dateText = Line(_plate, "MON 5 JAN 1987", LedgerStyle.Condensed, 11f, Ink,
                TextAlignmentOptions.MidlineLeft);
            _dateText.characterSpacing = 10f;
            _timeText = Line(_plate, "00:00", LedgerStyle.Mono, 11f, Red,
                TextAlignmentOptions.MidlineRight);

            _dateRect = _dateText.rectTransform;
            _timeRect = _timeText.rectTransform;
            _slower = Control("Slower", "<<", () => Step(faster: false));
            _speed = DemoUi.NewRect("Speed", _plate);
            _speedText = Line(_speed, "1X", LedgerStyle.Mono, 9f, Ink,
                TextAlignmentOptions.Center);
            DemoUi.Fill(_speedText.rectTransform);
            _faster = Control("Faster", ">>", () => Step(faster: true));
            _pause = Control("Pause", "II", TogglePause, out _pauseText, out _pauseFace);

            Layout();
            RefreshControls();
            BuildTimeScrubber();
        }

        /// <summary>The T readout's interactive half. It lives on the permanent clock
        /// canvas, above both the 3D city and TurfMap, so one slider controls the one
        /// shared DemoClock in either representation.</summary>
        void BuildTimeScrubber()
        {
            const float width = 460f, height = 46f;

            _timeScrubber = DemoUi.NewRect("Time Scrubber", transform);
            _timeScrubber.anchorMin = _timeScrubber.anchorMax = _timeScrubber.pivot =
                new Vector2(1f, 1f);
            _timeScrubber.anchoredPosition = new Vector2(-14f, -64f);
            _timeScrubber.sizeDelta = new Vector2(width, height);
            LedgerKit.Fill(_timeScrubber, Paper).raycastTarget = true;
            LedgerKit.Frame(_timeScrubber, 0.5f, Rule);

            var label = Line(_timeScrubber, "TIME", LedgerStyle.Condensed, 10f, Ink,
                TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 10f;
            LedgerKit.PlaceTopLeft(label.rectTransform, 10f, -12f, 54f, 22f);

            var track = DemoUi.NewRect("Hour", _timeScrubber);
            LedgerKit.PlaceTopLeft(track, 70f, -19f, 306f, 8f);
            var trackFace = LedgerKit.Fill(track, Key);
            trackFace.raycastTarget = true;
            LedgerKit.Frame(track, 0.5f, Rule);

            var fill = DemoUi.NewRect("Elapsed", track);
            DemoUi.Fill(fill, 1f);
            LedgerKit.Fill(fill, new Color(Red.r, Red.g, Red.b, 0.55f));

            var handle = DemoUi.NewRect("Handle", track);
            handle.sizeDelta = new Vector2(12f, 18f);
            var handleFace = LedgerKit.Fill(handle, Red);
            handleFace.raycastTarget = true;

            _timeSlider = track.gameObject.AddComponent<Slider>();
            _timeSlider.minValue = 0f;
            _timeSlider.maxValue = DemoClock.HoursPerDay;
            _timeSlider.wholeNumbers = false;
            _timeSlider.fillRect = fill;
            _timeSlider.handleRect = handle;
            _timeSlider.targetGraphic = handleFace;
            _timeSlider.direction = Slider.Direction.LeftToRight;
            _timeSlider.SetValueWithoutNotify(_clock != null ? _clock.Hour : 0f);
            _timeSlider.onValueChanged.AddListener(SetTimeFromSlider);

            _scrubTime = Line(_timeScrubber, "00:00", LedgerStyle.Mono, 11f, Red,
                TextAlignmentOptions.MidlineRight);
            LedgerKit.PlaceTopLeft(_scrubTime.rectTransform, 386f, -12f, 64f, 22f);

            _timeScrubber.gameObject.SetActive(false);
        }

        void SetTimeFromSlider(float hour)
        {
            if (_clock != null)
                _clock.SetHour(hour);
        }

        TMP_Text Line(Transform parent, string text, TMP_FontAsset face, float size,
            Color colour, TextAlignmentOptions alignment) =>
            LedgerKit.Line(parent, face, size, colour, 0f, 0f, 0f,
                LedgerKit.LineBox(size), text, alignment);

        RectTransform Control(string name, string label, UnityEngine.Events.UnityAction action) =>
            Control(name, label, action, out _, out _);

        RectTransform Control(string name, string label, UnityEngine.Events.UnityAction action,
            out TMP_Text text, out Image face)
        {
            var rect = DemoUi.NewRect(name, _plate);
            face = LedgerKit.Fill(rect, Key);
            face.raycastTarget = true;
            LedgerKit.Frame(rect, 0.5f, Rule);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(action);

            text = Line(rect, label, LedgerStyle.Condensed, 9f, Ink,
                TextAlignmentOptions.Center);
            text.characterSpacing = 8f;
            DemoUi.Fill(text.rectTransform);
            return rect;
        }

        void Layout()
        {
            if (_plate == null)
                return;

            float width = Width;
            _plate.sizeDelta = new Vector2(width, Height);
            float dateY = -(DateTall - LedgerKit.LineBox(11f)) * 0.5f;
            LedgerKit.PlaceTopLeft(_dateRect, Pad, dateY, width - Pad * 2f,
                LedgerKit.LineBox(11f));
            LedgerKit.PlaceTopLeft(_timeRect, Pad, dateY, width - Pad * 2f,
                LedgerKit.LineBox(11f));

            const float small = 30f, speed = 34f, pause = 36f, gap = 3f, tall = 18f;
            // The control ladder belongs to the same left reading edge as the date,
            // rather than floating in the middle of the narrow clock plate.
            float x = Pad;
            float y = -DateTall - 3f;
            LedgerKit.PlaceTopLeft(_slower, x, y, small, tall);
            x += small + gap;
            LedgerKit.PlaceTopLeft(_speed, x, y, speed, tall);
            x += speed + gap;
            LedgerKit.PlaceTopLeft(_faster, x, y, small, tall);
            x += small + gap;
            LedgerKit.PlaceTopLeft(_pause, x, y, pause, tall);
        }

        void TogglePause()
        {
            if (_clock == null)
                return;
            _clock.Paused = !_clock.Paused;
            RefreshControls();
        }

        void Step(bool faster)
        {
            if (_clock == null)
                return;
            if (faster)
                _clock.SpeedUp();
            else
                _clock.SlowDown();
            RefreshControls();
        }

        void RefreshControls()
        {
            if (_clock == null)
                return;

            if (_speedText)
                _speedText.text = _clock.SpeedMultiplier.ToString("0.#") + "X";
            if (_pauseText)
                _pauseText.text = _clock.Paused ? ">" : "II";
            if (_pauseFace)
                _pauseFace.color = _clock.Paused ? KeyOn : Key;
            if (_pauseText)
                _pauseText.color = _clock.Paused ? Red : Ink;
        }

        void LateUpdate()
        {
            if (_clock == null)
                return;

            if (_rig == null)
                _rig = FindAnyObjectByType<DemoCamera>();

            bool showScrubber = _rig != null && _rig.showZoom;
            if (_timeScrubber && _timeScrubber.gameObject.activeSelf != showScrubber)
                _timeScrubber.gameObject.SetActive(showScrubber);

            if (showScrubber)
            {
                _timeSlider.SetValueWithoutNotify(_clock.Hour);
                int scrubMinute = Mathf.FloorToInt(_clock.Hour * 60f) % (24 * 60);
                if (scrubMinute != _scrubMinute)
                {
                    _scrubMinute = scrubMinute;
                    _scrubTime.SetText("{0:00}:{1:00}", scrubMinute / 60, scrubMinute % 60);
                }
            }

            Layout();

            int minute = Mathf.FloorToInt(_clock.Hour * 60f);
            if (minute != _shownMinute)
            {
                _shownMinute = minute;
                _timeText.SetText("{0:00}:{1:00}", (minute / 60) % 24, minute % 60);
            }

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int day = outfit != null ? outfit.Campaign.Day : 1;
            if (day != _shownDay)
            {
                _shownDay = day;
                _dateText.text = LivingCity.News.NewsDate.FromClockDay(day - 1).Stamped();
            }
        }
    }
}
