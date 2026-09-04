using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.UI;

namespace RoadDemo
{
    /// <summary>
    /// The city's permanent time strip. It is a view of the shared
    /// <see cref="LivingCity.Ambient.CityClock"/>, not
    /// a map control: the same date, pause and speed ladder remain available while
    /// the player is on the street, looking at the turf plan, or using the ledger.
    /// </summary>
    public sealed class DemoClockHud : MonoBehaviour
    {
        /// <summary>The design's top bar: the clock strip and the crew chips beside it
        /// are the same 62 tall, so the two read as one bar bolted across the top rather
        /// than two panels that happen to touch.</summary>
        public const float Height = 62f;

        /// <summary>The strip's width, flat rather than a share of the frame - the design
        /// gives the clock 200 and the chips whatever is left. Public because the chip
        /// row starts where this ends.</summary>
        public const float PlateWidth = 200f;

        const float WidthFraction = 0.18f;
        /// <summary>The dark band across the head of the strip, carrying the date and
        /// the hour.</summary>
        const float BandTall = 22f;
        const float Pad = 9f;

        static readonly Color Paper = new Color32(244, 236, 214, 250);
        static readonly Color Ink = new Color32(43, 36, 24, 255);
        static readonly Color Rule = new Color32(43, 36, 24, 140);
        static readonly Color Red = new Color32(143, 33, 25, 255);
        static readonly Color Key = new Color32(43, 36, 24, 16);
        static readonly Color KeyOn = new Color32(143, 33, 25, 35);

        /// <summary>The night pass, shared with the panels beside it: the strip and
        /// the crew chips are one bar across the top and must cross together. It was
        /// two tables, and the segmented control's ink was in the other one - which is
        /// why the speed keys printed near-black on a near-black plate after dark.
        /// </summary>
        readonly HudNight _night = new HudNight();

        LivingCity.Ambient.CityClock _clock;
        Canvas _canvas;
        RectTransform _plate, _band, _dateRect, _timeRect, _speedBar;
        TMP_Text _dateText, _timeText;
        int _shownSpeed = int.MinValue;
        int _shownMinute = -1, _shownDay = -1;

        DemoCamera _rig;
        RectTransform _timeScrubber;
        Slider _timeSlider;
        TMP_Text _scrubTime;
        TMP_Text _fogText;
        StreetCutaway _occlusion;
        Button _occlusionButton;
        TMP_Text _occlusionText;
        int _scrubMinute = -1;

        public void Init(LivingCity.Ambient.CityClock clock)
        {
            _clock = clock;
            _rig = FindAnyObjectByType<DemoCamera>();
            _occlusion = FindAnyObjectByType<StreetCutaway>();
            Build();
        }

        float Width => PlateWidth;

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

            // Use the shared, fully opaque HUD alpha so the city cannot show through.
            gameObject.AddComponent<CanvasGroup>().alpha = HudNight.Alpha;

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

            // The head band: what day it is on the left, what hour on the right in the
            // rail's gold. The band is the design's own, and it is what makes the strip
            // the left END OF A BAR rather than a cream card floating in the corner.
            _band = DemoUi.NewRect("Band", _plate);
            LedgerKit.Fill(_band, LedgerV2.Head);

            _dateText = Line(_band, "MON 5 JAN 1987", LedgerStyle.Condensed, 13.9f,
                LedgerV2.HeadCream, TextAlignmentOptions.MidlineLeft);
            _dateText.characterSpacing = 12f;
            _timeText = Line(_band, "00:00", LedgerStyle.MonoBold, 14.4f,
                LedgerStyle.RailGold, TextAlignmentOptions.MidlineRight);

            _dateRect = _dateText.rectTransform;
            _timeRect = _timeText.rectTransform;

            Layout();
            BuildTimeScrubber();
            RefreshControls();
            _night.Register(transform);
        }

        /// <summary>The T readout's interactive half. It lives on the permanent clock
        /// canvas, above both the 3D city and TurfMap, so one slider controls the one
        /// shared CityClock in either representation.</summary>
        void BuildTimeScrubber()
        {
            const float width = 460f, height = 110f;

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
            _timeSlider.maxValue = LivingCity.Ambient.CityClock.HoursPerDay;
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

            var fog = DemoUi.NewRect("Fog Of War", _timeScrubber);
            LedgerKit.PlaceTopLeft(fog, 10f, -46f, width - 20f, 22f);
            var fogFace = LedgerKit.Fill(fog, Key);
            fogFace.raycastTarget = true;
            LedgerKit.Frame(fog, 0.5f, Rule);
            var fogButton = fog.gameObject.AddComponent<Button>();
            fogButton.targetGraphic = fogFace;
            fogButton.onClick.AddListener(ToggleFogOfWar);

            _fogText = Line(fog, "", LedgerStyle.Condensed, 10f, Red,
                TextAlignmentOptions.Center);
            _fogText.characterSpacing = 10f;
            DemoUi.Fill(_fogText.rectTransform);
            RefreshFogControl();

            var occlusion = DemoUi.NewRect("Occlusion", _timeScrubber);
            LedgerKit.PlaceTopLeft(occlusion, 10f, -78f, width - 20f, 22f);
            var occlusionFace = LedgerKit.Fill(occlusion, Key);
            occlusionFace.raycastTarget = true;
            LedgerKit.Frame(occlusion, 0.5f, Rule);
            _occlusionButton = occlusion.gameObject.AddComponent<Button>();
            _occlusionButton.targetGraphic = occlusionFace;
            _occlusionButton.onClick.AddListener(ToggleOcclusion);

            _occlusionText = Line(occlusion, "", LedgerStyle.Condensed, 10f, Red,
                TextAlignmentOptions.Center);
            _occlusionText.characterSpacing = 10f;
            DemoUi.Fill(_occlusionText.rectTransform);
            RefreshOcclusionControl();

            _timeScrubber.gameObject.SetActive(false);
        }

        void SetTimeFromSlider(float hour)
        {
            if (_clock != null)
                _clock.SetHour(hour);
        }

        void ToggleFogOfWar()
        {
            MapVisionRegistry.SetFogOfWarEnabled(!MapVisionRegistry.FogOfWarEnabled);
            RefreshFogControl();
        }

        void RefreshFogControl()
        {
            if (_fogText != null)
                _fogText.text = "FOG OF WAR: " +
                    (MapVisionRegistry.FogOfWarEnabled ? "ON" : "OFF");
        }

        void ToggleOcclusion()
        {
            if (_occlusion == null)
                return;

            _occlusion.SetOcclusionEnabled(!_occlusion.OcclusionEnabled);
            RefreshOcclusionControl();
        }

        void RefreshOcclusionControl()
        {
            bool available = _occlusion != null;
            if (_occlusionButton != null)
                _occlusionButton.interactable = available;
            if (_occlusionText != null)
                _occlusionText.text = available
                    ? "OCCLUSION: " + (_occlusion.OcclusionEnabled ? "ON" : "OFF")
                    : "OCCLUSION: N/A";
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
            LedgerKit.PlaceTopLeft(_band, 0f, 0f, width, BandTall);

            float line = LedgerKit.LineBox(13.9f);
            float y = -(BandTall - line) * 0.5f;
            LedgerKit.PlaceTopLeft(_dateRect, Pad, y, width - Pad * 2f, line);
            LedgerKit.PlaceTopLeft(_timeRect, Pad, y, width - Pad * 2f, line);
        }

        /// <summary>
        /// The speed control: one segmented bar, the design's own - a single question
        /// with a single answer, rather than two arrows to step toward the rung you
        /// wanted. HOLD is the last cell, because a held clock is a speed and not a
        /// mode. Rebuilt when the rung moves: a segmented bar carries its answer in its
        /// chrome, so there is nothing to merely re-tint.
        /// </summary>
        void BuildSpeedBar()
        {
            if (_clock == null || _plate == null)
                return;

            var rung = _clock.Paused ? _clock.SpeedCount : _clock.SpeedIndex;
            if (rung == _shownSpeed && _speedBar != null)
                return;
            _shownSpeed = rung;

            if (_speedBar != null)
                Destroy(_speedBar.gameObject);

            _speedBar = DemoUi.NewRect("Speed", _plate);
            float wide = Width - 14f;
            LedgerKit.PlaceTopLeft(_speedBar, 7f, -(BandTall + 4f), wide, 26f);

            var labels = new string[_clock.SpeedCount + 1];
            for (var i = 0; i < _clock.SpeedCount; i++)
                // The design's own multiplication sign, not a capital X: the rung is a
                // multiplier and the sign says so.
                labels[i] = _clock.SpeedAt(i).ToString("0.#") + "×";
            labels[_clock.SpeedCount] = "Hold";

            LedgerV2.Segmented(_speedBar, 0f, 0f, 26f, labels, rung, Pick,
                wide / labels.Length, 9.5f);

            // The bar is new paper, so it has to be taken into the night palette and
            // brought to wherever the sky is now - and the paper it replaced struck
            // from the register, or every rung change would leave a dead entry behind.
            _night.ForgetDead();
            _night.Register(_speedBar);
        }

        void Pick(int rung)
        {
            if (_clock == null)
                return;

            if (rung >= _clock.SpeedCount)
            {
                _clock.Paused = true;
            }
            else
            {
                _clock.Paused = false;
                _clock.SetSpeed(rung);
            }
            RefreshControls();
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

            BuildSpeedBar();
        }

        void LateUpdate()
        {
            if (_clock == null)
                return;

            _night.Relight();

            if (_rig == null)
                _rig = FindAnyObjectByType<DemoCamera>();

            bool showScrubber = _rig != null && _rig.showZoom;
            if (_timeScrubber && _timeScrubber.gameObject.activeSelf != showScrubber)
                _timeScrubber.gameObject.SetActive(showScrubber);

            if (showScrubber)
            {
                // H and this button own the same switch; keep the label honest when
                // the keyboard changed it while the debug panel was already open.
                RefreshOcclusionControl();
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
