using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RoadDemo
{
    // The strip across the very top of the screen, full width: the hour, the day,
    // and the time controls - pause/resume, slower, the current speed, faster.
    // Reads DemoClock and nothing else.
    //
    // Styled after the personnel ledger's CRT terminal: a dark green tube for the
    // bar, one green phosphor doing all the talking - bright for data (the time,
    // the speed), dim for chrome (labels, frames) - amber reserved for "something
    // demands attention" (the PAUSE state), scanlines over the glass, and not a
    // sprite anywhere: every element is a plain Image block or a 1px frame. The
    // colour values are the ledger's, copied here so the demo stays self-contained.
    public class DemoTopBar : MonoBehaviour
    {
        public DemoClock clock;

        const float BarHeight = 42f;

        // -- the ledger's phosphor vocabulary, demo-local copy -----------------
        static readonly Color Screen = new Color(0.015f, 0.055f, 0.025f, 0.97f);
        static readonly Color Phosphor = new Color(0.38f, 1f, 0.50f);
        static readonly Color PhosphorDim = new Color(0.22f, 0.55f, 0.30f);
        static readonly Color ButtonGlow = new Color(0.38f, 1f, 0.50f, 0.16f);
        static readonly Color Amber = new Color(1f, 0.72f, 0.20f);
        static readonly Color ScanLine = new Color(0f, 0f, 0f, 0.28f);

        // button tint states - the tint multiplies the face, so normal sits below
        // 1 and hover IS the full glow
        static readonly Color ButtonNormal = new Color(0.78f, 0.78f, 0.78f);
        static readonly Color ButtonHover = Color.white;
        static readonly Color ButtonPressed = new Color(0.5f, 0.5f, 0.5f);

        TMP_Text _timeLabel;
        TMP_Text _dayLabel;
        TMP_Text _speedLabel;
        TMP_Text _pauseLabel;

        Texture2D _scanTex;
        int _shownMinute = -1;
        int _shownDay = -1;

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
            // the tube: full width, glued to the very top edge
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(transform, false);
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, BarHeight);

            var tube = bar.AddComponent<Image>();
            tube.color = Screen;
            tube.raycastTarget = false;

            BuildScanLines(bar.transform);

            // the ruled line along the bar's bottom edge - the terminal's own
            // statement of where the screen ends
            var rule = Block(bar.transform, "Rule", new Color(
                PhosphorDim.r, PhosphorDim.g, PhosphorDim.b, 0.6f));
            var ruleRect = (RectTransform)rule.transform;
            ruleRect.anchorMin = new Vector2(0f, 0f);
            ruleRect.anchorMax = new Vector2(1f, 0f);
            ruleRect.pivot = new Vector2(0.5f, 0f);
            ruleRect.sizeDelta = new Vector2(0f, 1f);

            // content row: time and day on the left, the controls on the right
            var row = new GameObject("Content", typeof(RectTransform));
            row.transform.SetParent(bar.transform, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = rowRect.offsetMax = Vector2.zero;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 7);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            _timeLabel = BuildLabel(row.transform, 78f, 24f, Phosphor);
            _timeLabel.text = "00:00";

            _dayLabel = BuildLabel(row.transform, 64f, 15f, PhosphorDim);
            _dayLabel.text = "DAY 1";
            _dayLabel.alignment = TextAlignmentOptions.MidlineLeft;

            // everything after this is pushed to the right edge
            var spring = new GameObject("Spring", typeof(RectTransform));
            spring.transform.SetParent(row.transform, false);
            spring.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _pauseLabel = BuildButton(row.transform, 44f, "II", TogglePause);
            BuildButton(row.transform, 44f, "<<", () => { if (clock) clock.SlowDown(); Refresh(); });
            _speedLabel = BuildLabel(row.transform, 64f, 18f, Phosphor);
            _speedLabel.text = "1x";
            BuildButton(row.transform, 44f, ">>", () => { if (clock) clock.SpeedUp(); Refresh(); });
        }

        // The dark line a CRT draws between raster rows: a 1x3 texture (two clear
        // rows, one dark) repeated down the bar. A RawImage with a uvRect tiles in
        // ONE quad - Image.Tiled would emit geometry per 1px tile.
        void BuildScanLines(Transform parent)
        {
            _scanTex = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _scanTex.SetPixels(new[] { Color.clear, Color.clear, ScanLine });
            _scanTex.Apply(false, true);

            var go = new GameObject("ScanLines", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var raw = go.AddComponent<RawImage>();
            raw.texture = _scanTex;
            raw.uvRect = new Rect(0f, 0f, 1f, BarHeight / 3f);
            raw.raycastTarget = false;
        }

        void TogglePause()
        {
            if (clock)
                clock.Paused = !clock.Paused;
            Refresh();
        }

        void Refresh()
        {
            bool paused = clock && clock.Paused;

            if (_pauseLabel)
                _pauseLabel.text = paused ? ">" : "II";

            // the speed readout is data - bright phosphor - until the clock is
            // held, and a held clock is the amber state: it demands attention
            if (_speedLabel && clock)
            {
                _speedLabel.text = paused ? "PAUSED" : clock.SpeedMultiplier + "x";
                _speedLabel.color = paused ? Amber : Phosphor;
            }
        }

        void LateUpdate()
        {
            if (!clock)
                return;

            int minute = Mathf.FloorToInt(clock.Hour * 60f);
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

        // ------------------------------------------------------------- elements

        static Image Block(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        TMP_Text BuildLabel(Transform parent, float width, float size, Color colour)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = BarHeight - 13f;

            // default TMP face (LiberationSans) with a little tracking - the
            // ledger's terminal voice; no font asset dependency
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.characterSpacing = 3f;
            text.alignment = TextAlignmentOptions.Midline;
            text.color = colour;
            text.raycastTarget = false;
            return text;
        }

        // A ledger button: a translucent phosphor block in a 1px frame. Three
        // layers - the frame (dim phosphor), the tube showing through inside it,
        // and the glow face the Button tints for hover and press.
        TMP_Text BuildButton(Transform parent, float width,
            string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = BarHeight - 14f;

            var frame = go.AddComponent<Image>();
            frame.color = PhosphorDim;
            frame.raycastTarget = true;

            var inner = Block(go.transform, "Tube", new Color(
                Screen.r, Screen.g, Screen.b, 1f));
            Inset((RectTransform)inner.transform, 1f);

            var face = Block(go.transform, "Face", ButtonGlow);
            Inset((RectTransform)face.transform, 1f);
            face.raycastTarget = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = ButtonNormal;
            colours.highlightedColor = ButtonHover;
            colours.selectedColor = ButtonHover;
            colours.pressedColor = ButtonPressed;
            button.colors = colours;
            button.onClick.AddListener(onClick);

            var text = BuildLabel(go.transform, width, 15f, Phosphor);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            Destroy(text.GetComponent<LayoutElement>());
            text.text = label;
            return text;
        }

        static void Inset(RectTransform rect, float border)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(border, border);
            rect.offsetMax = new Vector2(-border, -border);
        }

        void OnDestroy()
        {
            if (_scanTex)
                Destroy(_scanTex);
        }
    }
}
