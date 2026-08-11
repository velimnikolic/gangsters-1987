using System.Collections.Generic;
using LivingCity.CameraRig;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    // Anything the police overlay hangs a dot over: a patrol car or an officer
    // on foot. Dimmed while the subject rests at the station; the title and line
    // feed the click-to-inspect popup.
    public interface IPatrolMarker
    {
        Transform MarkerTf { get; }
        float MarkerHeight { get; }
        bool MarkerDimmed { get; }

        /// <summary>"Patrol Car 2" / "Officer 1" - the popup's first line.</summary>
        string MarkerTitle { get; }

        /// <summary>What the unit is doing and where it is going, as a sentence.</summary>
        string MarkerLine { get; }
    }

    /// <summary>Compass words for the popup's "where is it going" line.</summary>
    public static class PatrolInfo
    {
        public static string Heading(Transform tf) => Compass(tf.forward);

        /// <summary>The compass word for the direction from here to there.</summary>
        public static string Toward(Vector3 from, Vector3 to) => Compass(to - from);

        static string Compass(Vector3 d)
        {
            // eight winds: diagonals matter when the destination is a district away
            float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(angle / 45f) & 7;
            return sector switch
            {
                0 => "north", 1 => "north-east", 2 => "east", 3 => "south-east",
                4 => "south", 5 => "south-west", 6 => "west", _ => "north-west",
            };
        }
    }

    // The blue indicator riding above every patrol unit: a small dot - the white
    // glow-dot sprite from the Synty Interface Modern Menus pack (the project's
    // single source of UI art) tinted police-blue - drawn with the main game's
    // CityOverlayHud trick: ScreenSpaceOverlay means a UI transform.position IS
    // screen pixels, so WorldToScreenPoint feeds it directly, it stays crisp at
    // every zoom and needs no billboarding.
    //
    // Click-to-inspect: a left click within PickRadius of a unit (its dot or its
    // body) selects it and opens a popup - the pack's dark box, 9-sliced - that
    // follows the unit and words what it is doing and where it is going. A click
    // that selects nothing, or Escape, closes it. Picking is screen-space
    // distance, no physics: the demo strips every collider off its agents, and
    // clicks claimed here are vetoed out of BuildingCardPicker so a click on a
    // cop does not also open the card of the block behind him.
    public class PolicePatrolOverlay : MonoBehaviour
    {
        const float MarkerSize = 14f;  // reference pixels on the 1080p design height
        const float BobAmplitude = 3f; // reference pixels of idle float
        const float BobPeriod = 1.8f;
        const float SelectedScale = 1.5f;

        const float PickRadius = 30f;  // reference pixels of click slack
        const float PopupWidth = 300f;
        const float PopupHeight = 64f;
        const float PopupLift = 26f;   // reference pixels above the dot

        // The sprite is white, so the tint IS the colour; a resting unit's dot
        // just fades back so the working shift reads at a glance.
        static readonly Color OnDuty = new Color(0.30f, 0.58f, 1f, 1f);
        static readonly Color Resting = new Color(0.30f, 0.58f, 1f, 0.4f);

        List<IPatrolMarker> _subjects;
        readonly List<Image> _images = new List<Image>();
        Canvas _canvas;
        Camera _cam;

        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupLine;
        int _selected = -1;
        string _shownTitle, _shownLine;

        public void Init(List<IPatrolMarker> subjects, Sprite dot, Sprite panel)
        {
            _subjects = subjects;

            var root = new GameObject("Police Overlay", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            for (int i = 0; i < subjects.Count; i++)
            {
                var go = new GameObject("indicator", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = dot;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(MarkerSize, MarkerSize);
                img.enabled = false;
                _images.Add(img);
            }

            BuildPopup(panel);
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = null;
        }

        // Built once, active, then hidden - the main HUD's trick: a TMP text only
        // loads its font in OnEnable, which never runs under an inactive parent.
        void BuildPopup(Sprite panel)
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - patrol dots still " +
                                 "show, but the click popup is disabled.");
                return;
            }

            _popup = new GameObject("Popup", typeof(RectTransform));
            _popup.transform.SetParent(_canvas.transform, false);
            _popupRect = (RectTransform)_popup.transform;
            _popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            _popupRect.pivot = new Vector2(0.5f, 0f);

            var background = _popup.AddComponent<Image>();
            background.raycastTarget = false;
            if (panel != null)
            {
                background.sprite = panel;
                background.type = Image.Type.Sliced;
                // the pack's 512px box carries a 180px authored border - at popup
                // scale that is the whole image, so shrink the drawn border down
                background.pixelsPerUnitMultiplier = 14f;
            }
            else
            {
                background.color = new Color(0f, 0f, 0f, 0.78f);
            }

            _popupTitle = BuildPopupText("Title", top: true);
            _popupTitle.fontSize = 15f;
            _popupTitle.fontStyle = FontStyles.Bold;
            _popupTitle.color = new Color(0.96f, 0.96f, 0.96f);

            _popupLine = BuildPopupText("Line", top: false);
            _popupLine.fontSize = 12.5f;
            _popupLine.color = new Color(0.82f, 0.86f, 0.93f);

            _popup.SetActive(false);
        }

        TMP_Text BuildPopupText(string name, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_popup.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rect.offsetMin = new Vector2(14f, top ? 0f : 8f);
            rect.offsetMax = new Vector2(-14f, top ? -8f : 0f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        bool _claimedThisFrame;

        // BuildingCardPicker asks before raycasting a click into the blocks; the
        // pick itself happens here so both systems agree on who owns the click.
        bool ClaimsClick(Vector2 screen)
        {
            _claimedThisFrame = true;
            int hit = PickAt(screen);
            Select(hit);
            return hit >= 0;
        }

        int PickAt(Vector2 screen)
        {
            if (_subjects == null || _cam == null) return -1;

            float radius = PickRadius * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            int best = -1;
            for (int i = 0; i < _subjects.Count; i++)
            {
                var tf = _subjects[i].MarkerTf;
                if (tf == null) continue;

                // the dot or the body, whichever the click lands nearer
                var body = _cam.WorldToScreenPoint(tf.position + Vector3.up * 0.9f);
                var dotP = _cam.WorldToScreenPoint(
                    tf.position + Vector3.up * _subjects[i].MarkerHeight);
                foreach (var p in new[] { body, dotP })
                {
                    if (p.z <= 0f) continue;
                    float d = ((Vector2)p - screen).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        void Select(int index)
        {
            _selected = index;
            _shownTitle = _shownLine = null;
            if (_popup != null)
                _popup.SetActive(index >= 0);
        }

        void LateUpdate()
        {
            if (_subjects == null) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            // Clicks normally arrive through ClaimsClick (BuildingCardPicker runs
            // first and asks); this is the fallback for a scene without the picker.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !_claimedThisFrame)
                Select(PickAt(mouse.position.ReadValue()));
            _claimedThisFrame = false;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                Select(-1);

            float w = Screen.width, h = Screen.height;
            for (int i = 0; i < _subjects.Count; i++)
            {
                var subject = _subjects[i];
                var img = _images[i];
                var tf = subject.MarkerTf;
                if (tf == null)
                {
                    if (img.enabled) img.enabled = false;
                    if (_selected == i) Select(-1);
                    continue;
                }

                var screen = _cam.WorldToScreenPoint(
                    tf.position + Vector3.up * subject.MarkerHeight);
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                if (img.enabled != on) img.enabled = on;
                if (!on) continue;

                // phase-shifted per unit so a parked row does not bounce in unison
                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + i * 1.3f)
                    * BobAmplitude * _canvas.scaleFactor;
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = subject.MarkerDimmed ? Resting : OnDuty;
                img.rectTransform.localScale =
                    Vector3.one * (_selected == i ? SelectedScale : 1f);
            }

            UpdatePopup(w, h);
        }

        void UpdatePopup(float w, float h)
        {
            if (_popup == null || _selected < 0) return;

            var subject = _subjects[_selected];
            var tf = subject.MarkerTf;
            if (tf == null)
            {
                Select(-1);
                return;
            }

            var title = subject.MarkerTitle;
            var line = subject.MarkerLine;
            if (title != _shownTitle) { _shownTitle = title; _popupTitle.text = title; }
            if (line != _shownLine) { _shownLine = line; _popupLine.text = line; }

            var screen = _cam.WorldToScreenPoint(
                tf.position + Vector3.up * subject.MarkerHeight);
            if (screen.z <= 0f)
            {
                _popup.SetActive(false);
                return;
            }
            if (!_popup.activeSelf)
                _popup.SetActive(true);

            float scale = _canvas.scaleFactor;
            float halfWidth = PopupWidth * 0.5f * scale;
            float height = PopupHeight * scale;
            _popupRect.position = new Vector3(
                Mathf.Clamp(screen.x, halfWidth, w - halfWidth),
                Mathf.Clamp(screen.y + PopupLift * scale, 0f, h - height),
                0f);
        }
    }
}
