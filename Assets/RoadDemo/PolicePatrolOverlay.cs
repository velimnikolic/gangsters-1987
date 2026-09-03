using System.Collections.Generic;
using LivingCity.CameraRig;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    // Anything the police overlay tracks: a patrol car or an officer on foot.
    // Cars keep the overhead dot; foot officers use the shared ground bracket for
    // hover/selection. Dimmed while the subject rests at the station; the title and
    // line feed the click-to-inspect popup.
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

    // The blue indicator for every patrol unit: cars get the overhead glow-dot
    // sprite, foot patrols get the same floor-corner brackets CityOverlayHud uses.
    // Both are drawn with the main game's ScreenSpaceOverlay trick: a UI
    // transform.position IS screen pixels, so WorldToScreenPoint feeds it directly,
    // it stays crisp at every zoom and needs no billboarding.
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
        const float HoverInterval = 0.1f;

        const float PickRadius = 30f;  // reference pixels of click slack
        const float PopupWidth = 320f;
        const float PopupHeight = 74f;
        const float PopupLift = 26f;   // reference pixels above the dot

        // The sprite is white, so the tint IS the colour. The duty blue is DemoUi's
        // own accent pushed a shade deeper - a dot over the world has to hold its
        // own against daylight asphalt where a bar's accent rule never does - and a
        // resting unit's dot just fades back so the working shift reads at a glance.
        static readonly Color OnDuty = new Color(0.38f, 0.70f, 1f, 1f);
        static readonly Color Resting = new Color(0.38f, 0.70f, 1f, 0.38f);

        List<IPatrolMarker> _subjects;
        readonly List<Image> _images = new List<Image>();
        readonly List<GroundBracketGraphic> _brackets = new List<GroundBracketGraphic>();
        Canvas _canvas;
        Camera _cam;

        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupLine;
        int _selected = -1;
        int _hovered = -1;
        float _nextHoverAt;
        string _shownTitle, _shownLine;
        readonly Vector3[] _bracketWorld = new Vector3[4];
        readonly Vector2[] _bracketLocal = new Vector2[4];

        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen ||
                                TurfMapHud.IsOpen;

        public void Init(List<IPatrolMarker> subjects)
        {
            var dot = DemoUi.Dot;
            if (dot == null)
            {
                // A sprite-less Image would draw a hard white square over every cop -
                // worse than no indicator at all, so the overlay sits the demo out.
                Debug.LogWarning("[RoadDemo] Modern Menus glow dot missing; patrol " +
                                 "indicators are off.");
                Destroy(this);
                return;
            }

            _subjects = subjects;

            var root = new GameObject("Police Overlay", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            EnsureIndicators(subjects.Count);

            BuildPopup();
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void EnsureIndicators(int count)
        {
            var dot = DemoUi.Dot;
            while (_images.Count < count)
            {
                var go = new GameObject("indicator", typeof(RectTransform));
                go.transform.SetParent(_canvas.transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = dot;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(MarkerSize, MarkerSize);
                img.enabled = false;
                _images.Add(img);

                var bracket = new GameObject("ground bracket", typeof(RectTransform))
                    .AddComponent<GroundBracketGraphic>();
                bracket.transform.SetParent(_canvas.transform, false);
                bracket.raycastTarget = false;
                var rect = bracket.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                bracket.enabled = false;
                _brackets.Add(bracket);
            }

            // Subjects can also shrink when a dead permanent beat leaves the force's
            // books. Reuse its UI slot later, but do not leave the old dot on screen.
            for (var i = count; i < _images.Count; i++)
            {
                _images[i].enabled = false;
                _brackets[i].enabled = false;
            }
            if (_selected >= count) Select(-1);
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = null;
        }

        // Built once, active, then hidden - the main HUD's trick: a TMP text only
        // loads its font in OnEnable, which never runs under an inactive parent.
        //
        // The card wears the demo's wardrobe: the same framed pack box the ledger's
        // detail card wears, the same two type faces, and the accent stripe down its
        // left edge that says "this is the selected thing" in the one colour the top
        // bar already uses for live.
        void BuildPopup()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - patrol dots still " +
                                 "show, but the click popup is disabled.");
                return;
            }

            _popupRect = DemoUi.NewRect("Popup", _canvas.transform);
            _popup = _popupRect.gameObject;
            _popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            _popupRect.pivot = new Vector2(0.5f, 0f);

            var background = _popup.AddComponent<Image>();
            background.raycastTarget = false;
            DemoUi.Dress(background, DemoUi.Box, 15f, DemoUi.Panel);

            var stripe = DemoUi.Block(_popupRect, "Accent", DemoUi.Accent);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(14f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -24f);

            _popupTitle = BuildPopupText("Title", 15f, DemoUi.Ink, top: true);
            _popupTitle.characterSpacing = 2f;

            _popupLine = BuildPopupText("Line", 13f, DemoUi.InkDim, top: false);

            _popup.SetActive(false);
        }

        TMP_Text BuildPopupText(string name, float size, Color colour, bool top)
        {
            var text = DemoUi.Text(_popupRect, name, size, colour,
                TextAlignmentOptions.MidlineLeft, display: top);

            // Left inset clears the accent stripe; the halves split the card's height
            // so the title sits over its own line with no measuring.
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rect.offsetMin = new Vector2(26f, top ? 0f : 10f);
            rect.offsetMax = new Vector2(-16f, top ? -10f : 0f);
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
                if (tf == null ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(tf.position))
                    continue;

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
            EnsureIndicators(_subjects.Count);
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            // Clicks normally arrive through ClaimsClick (BuildingCardPicker runs
            // first and asks); this is the fallback for a scene without the picker -
            // and for the frames the picker stands down, which is the whole time the
            // ledger is open. A click spent on a screen never reaches a cop either.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !_claimedThisFrame &&
                !BookOpen && !PointerOverUi())
                Select(PickAt(mouse.position.ReadValue()));
            _claimedThisFrame = false;

            var pointerBlocked = mouse == null || BookOpen || PointerOverUi();
            if (pointerBlocked)
            {
                _hovered = -1;
            }
            else if (Time.unscaledTime >= _nextHoverAt)
            {
                _nextHoverAt = Time.unscaledTime + HoverInterval;
                _hovered = PickAt(mouse.position.ReadValue());
            }

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                Select(-1);

            float w = Screen.width, h = Screen.height;
            for (int i = 0; i < _subjects.Count; i++)
            {
                var subject = _subjects[i];
                var img = _images[i];
                var bracket = _brackets[i];
                var tf = subject.MarkerTf;
                if (tf == null ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(tf.position))
                {
                    if (img.enabled) img.enabled = false;
                    if (bracket.enabled) bracket.enabled = false;
                    if (_selected == i) Select(-1);
                    continue;
                }

                var screen = _cam.WorldToScreenPoint(
                    tf.position + Vector3.up * subject.MarkerHeight);
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                var selected = _selected == i;
                var footPatrol = subject is PoliceBeat;
                var bracketOn = footPatrol && on && (_hovered == i || selected) &&
                                UpdateGroundBracket(bracket, tf, selected);
                if (!bracketOn && bracket.enabled)
                    bracket.enabled = false;

                var dotOn = on && !bracketOn;
                if (img.enabled != dotOn) img.enabled = dotOn;
                if (!dotOn) continue;

                // phase-shifted per unit so a parked row does not bounce in unison
                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + i * 1.3f)
                    * BobAmplitude * _canvas.scaleFactor;
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = subject.MarkerDimmed ? Resting : OnDuty;
                img.rectTransform.localScale =
                    Vector3.one * (selected ? SelectedScale : 1f);
            }

            UpdatePopup(w, h);
        }

        static bool PointerOverUi()
        {
            return UnityEngine.EventSystems.EventSystem.current &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        bool UpdateGroundBracket(GroundBracketGraphic bracket, Transform target, bool selected)
        {
            if (!bracket || !target)
                return false;

            if (!HumanGroundBracket.TryProject(
                    _cam, (RectTransform)_canvas.transform, target,
                    _bracketWorld, _bracketLocal,
                    Screen.width, Screen.height))
            {
                if (bracket.enabled) bracket.enabled = false;
                return false;
            }

            if (!bracket.enabled)
                bracket.enabled = true;
            bracket.Set(
                _bracketLocal,
                HumanGroundBracket.ArmLength(selected, false, Time.unscaledTime),
                HumanGroundBracket.Thickness,
                HumanGroundBracket.Tint(false));
            return true;
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

            // Anchored to the officer, and gone the moment he is off the screen - a card
            // clamped into the viewport follows the player instead of its subject.
            float scale = _canvas.scaleFactor;
            if (!LivingCity.UI.OverlayCard.TryPlace(
                    _cam, tf.position + Vector3.up * subject.MarkerHeight,
                    PopupLift * scale, new Vector2(PopupWidth * scale, PopupHeight * scale),
                    w, h, out var where))
            {
                if (_popup.activeSelf) _popup.SetActive(false);
                return;
            }
            if (!_popup.activeSelf)
                _popup.SetActive(true);
            _popupRect.position = where;
        }
    }
}
