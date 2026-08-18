using System.Collections.Generic;
using LivingCity.CameraRig;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    // The indicators riding above the crews' men - gold for the outfit, red for a
    // rival mob; the lieutenant's dot larger with his surname over it, his hoods'
    // smaller and dimmer - and the two clicks that command them: a left click
    // within PickRadius of any man of the outfit selects his crew (the popup names
    // the lieutenant and words what the crew is doing); a right click - a click,
    // not the camera's right-drag orbit - sends the selected lieutenant to the
    // point (the sidewalk nearest it in the city) with his hoods after him, or, on
    // a rival's man, sends the crew at that rival. Same ScreenSpaceOverlay trick as
    // the police overlay: a UI transform.position IS screen pixels, so
    // WorldToScreenPoint feeds it.
    //
    // Left clicks come through BuildingCardPicker's veto - chained behind the
    // police overlay's, which registered first - so a click on a man opens no
    // building card and a click on a cop selects the cop, not a crew behind him.
    public class CrewOverlay : MonoBehaviour
    {
        const float BossSize = 17f;    // reference pixels on the 1080p design height
        const float HoodSize = 10f;
        const float BobAmplitude = 3f;
        const float BobPeriod = 1.8f;
        const float SelectedScale = 1.45f;
        const float TagLift = 12f;     // surname over the boss dot

        const float PickRadius = 30f;
        const float PopupWidth = 360f;
        const float PopupHeight = 74f;
        const float PopupLift = 44f;

        const float ClickSlackPx = 8f; // a right-drag beyond this is the camera's
        const float ClickHold = 0.45f;
        const float MarkLife = 0.9f;   // the order mark's fade on the ground

        static readonly Color BossOn = new Color(1f, 0.78f, 0.32f, 1f);
        static readonly Color HoodOn = new Color(1f, 0.78f, 0.32f, 0.6f);
        static readonly Color MarkTint = new Color(1f, 0.78f, 0.32f, 0.9f);
        static readonly Color RivalBoss = new Color(1f, 0.36f, 0.30f, 1f);
        static readonly Color RivalHood = new Color(1f, 0.36f, 0.30f, 0.6f);
        static readonly Color AttackTint = new Color(1f, 0.36f, 0.30f, 0.9f);

        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen;

        DemoCrews _crews;
        Canvas _canvas;
        Camera _cam;
        Transform _dotRoot;
        readonly List<Image> _dots = new List<Image>();
        readonly List<TMP_Text> _tags = new List<TMP_Text>();
        readonly List<Image> _glyphs = new List<Image>(); // the activity sign beside a boss's dot
        readonly List<CrewWalker> _men = new List<CrewWalker>();
        readonly List<bool> _menBoss = new List<bool>();
        readonly List<DemoCrews.Unit> _menUnit = new List<DemoCrews.Unit>();

        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupLine;
        string _shownTitle, _shownLine;

        Image _mark;
        (string text, float until) _refusal;
        readonly List<Image> _carDots = new List<Image>();
        readonly List<TMP_Text> _carTags = new List<TMP_Text>();
        Color _markTint = MarkTint;
        Vector3 _markWorld;
        float _markAge = MarkLife;

        System.Func<Vector2, bool> _previousVeto;
        bool _claimedThisFrame;
        Vector2 _rightDown;
        float _rightDownAt;
        bool _rightPending;

        public void Init(DemoCrews crews)
        {
            _crews = crews;
            var dot = DemoUi.Dot;
            if (dot == null)
            {
                Debug.LogWarning("[RoadDemo] Modern Menus glow dot missing; crew " +
                                 "indicators are off.");
                Destroy(this);
                return;
            }

            var root = new GameObject("Crew Overlay", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // over the police overlay's default 0, so a selected crew's card is never
            // hidden under a cop's dot
            _canvas.sortingOrder = 1;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _dotRoot = new GameObject("Dots", typeof(RectTransform)).transform;
            _dotRoot.SetParent(root.transform, false);

            _mark = new GameObject("Order Mark", typeof(RectTransform)).AddComponent<Image>();
            _mark.transform.SetParent(root.transform, false);
            _mark.sprite = dot;
            _mark.raycastTarget = false;
            _mark.rectTransform.sizeDelta = new Vector2(22f, 22f);
            _mark.enabled = false;

            BuildPopup();

            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
        }

        // ------------------------------------------------------------------ chrome

        void BuildPopup()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - crew dots still show, " +
                                 "but the name tags and click popup are disabled.");
                return;
            }

            _popupRect = DemoUi.NewRect("Popup", _canvas.transform);
            _popup = _popupRect.gameObject;
            _popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            _popupRect.pivot = new Vector2(0.5f, 0f);

            var background = _popup.AddComponent<Image>();
            background.raycastTarget = false;
            DemoUi.Dress(background, DemoUi.Box, 15f, DemoUi.Panel);

            var stripe = DemoUi.Block(_popupRect, "Accent", DemoUi.Gold);
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
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rect.offsetMin = new Vector2(26f, top ? 0f : 10f);
            rect.offsetMax = new Vector2(-16f, top ? -10f : 0f);
            return text;
        }

        // one dot (and one tag, used only over lieutenants) per man, grown on demand
        void EnsureSlots(int count)
        {
            while (_dots.Count < count)
            {
                var go = new GameObject("indicator", typeof(RectTransform));
                go.transform.SetParent(_dotRoot, false);
                var img = go.AddComponent<Image>();
                img.sprite = DemoUi.Dot;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(BossSize, BossSize);
                img.enabled = false;
                _dots.Add(img);

                TMP_Text tag = null;
                if (_popup != null)
                {
                    tag = DemoUi.Text(_dotRoot, "tag", 11f, BossOn, TextAlignmentOptions.Bottom,
                        display: true);
                    tag.characterSpacing = 3f;
                    tag.rectTransform.pivot = new Vector2(0.5f, 0f);
                    tag.rectTransform.sizeDelta = new Vector2(240f, 16f);
                    tag.enabled = false;
                }
                _tags.Add(tag);

                var glyph = DemoUi.Icon(_dotRoot, "glyph", null, 16f, Color.clear);
                glyph.enabled = false;
                _glyphs.Add(glyph);
            }
        }

        // ------------------------------------------------------------------ picking

        bool ClaimsClick(Vector2 screen)
        {
            _claimedThisFrame = true;
            // a click on the crew bar is the bar's (it selects there); the street keeps out
            if (CrewBar.Instance != null && CrewBar.Instance.Contains(screen)) return true;
            if (_previousVeto != null && _previousVeto(screen))
            {
                _crews.Select(null);
                return true;
            }
            var unit = PickAt(screen);
            // a click on the outfit's car picks the crew that owns it (or rides in it)
            if (unit == null)
            {
                var car = PickCarAt(screen);
                if (car != null)
                {
                    var owner = car.Occupant ?? car.Owner;
                    if (owner != null && owner.Faction == 0) _crews.Select(owner);
                    return true;
                }
            }
            // a click on a rival is a look, not a choice: the outfit's selection stands
            if (unit != null && unit.Faction != 0) return true;
            _crews.Select(unit);
            return unit != null;
        }

        DemoCrews.Unit PickAt(Vector2 screen)
        {
            if (_cam == null) return null;
            float radius = PickRadius * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            DemoCrews.Unit best = null;
            for (int i = 0; i < _men.Count; i++)
            {
                var tf = _men[i].Tf;
                if (tf == null || _men[i].Dead || _crews.IsAboard(_men[i])) continue;
                var body = _cam.WorldToScreenPoint(tf.position + Vector3.up * 0.9f);
                var dotP = _cam.WorldToScreenPoint(tf.position + Vector3.up * MarkerHeight(_men[i]));
                foreach (var p in new[] { body, dotP })
                {
                    if (p.z <= 0f) continue;
                    float d = ((Vector2)p - screen).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = _menUnit[i]; }
                }
            }
            return best;
        }

        static float MarkerHeight(CrewWalker man) => man.IsLieutenant ? 2.25f : 2.05f;

        /// <summary>The outfit's car under the pointer - within the same slack as a man,
        /// measured to its roof.</summary>
        CrewCar PickCarAt(Vector2 screen)
        {
            if (_cam == null) return null;
            float radius = (PickRadius + 12f) * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            CrewCar best = null;
            foreach (var car in _crews.Cars)
            {
                if (car.Tf == null) continue;
                var p = _cam.WorldToScreenPoint(car.Position + Vector3.up * 0.9f);
                if (p.z <= 0f) continue;
                float d = ((Vector2)p - screen).sqrMagnitude;
                if (d < bestD) { bestD = d; best = car; }
            }
            return best;
        }

        static bool PointerOverUi() =>
            (UnityEngine.EventSystems.EventSystem.current &&
             UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) ||
            (CrewBar.Instance != null && Mouse.current != null &&
             CrewBar.Instance.Contains(Mouse.current.position.ReadValue()));

        // A right click - pressed and released in place, quickly - is an order; a
        // right-drag is DemoCamera's orbit and must not also send anyone anywhere.
        void ReadRightClick(Mouse mouse)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _rightPending = !BookOpen && !PointerOverUi();
                _rightDown = mouse.position.ReadValue();
                _rightDownAt = Time.unscaledTime;
                return;
            }
            if (!_rightPending || !mouse.rightButton.wasReleasedThisFrame) return;
            _rightPending = false;

            var up = mouse.position.ReadValue();
            float slack = ClickSlackPx * (_canvas != null ? _canvas.scaleFactor : 1f);
            if ((up - _rightDown).sqrMagnitude > slack * slack ||
                Time.unscaledTime - _rightDownAt > ClickHold)
                return;
            if (BookOpen || _crews.Selected == null) return;

            // the outfit's car under the click: get in (or out) if it is this crew's
            var car = PickCarAt(up);
            if (car != null)
            {
                if (_crews.OrderCar(car))
                    ShowMark(car.Position + Vector3.up * 1.0f, MarkTint);
                else if (_crews.CarRefusal != null)
                    _refusal = (_crews.CarRefusal, Time.unscaledTime + 2.5f);
                return;
            }

            // a rival's man under the click: the crew goes for his crew
            var picked = PickAt(up);
            if (picked != null && picked.Faction != 0)
            {
                if (_crews.OrderAttack(picked))
                    ShowMark(picked.Position + Vector3.up * 1.2f, AttackTint);
                return;
            }

            // the ground all sits on one plane; the pick lands there
            var plane = new Plane(Vector3.up, new Vector3(0f, _crews.GroundY, 0f));
            var ray = _cam.ScreenPointToRay(up);
            if (!plane.Raycast(ray, out float enter)) return;
            var world = ray.GetPoint(enter);
            if (_crews.OrderSelected(world, out var destination))
                ShowMark(destination, MarkTint);
        }

        void ShowMark(Vector3 world, Color tint)
        {
            _markWorld = world;
            _markTint = tint;
            _markAge = 0f;
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            if (_crews == null) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            // roll call: every man out, lieutenants and hoods, with the crew he answers to
            _men.Clear();
            _menBoss.Clear();
            _menUnit.Clear();
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                {
                    _men.Add(man);
                    _menBoss.Add(man.IsLieutenant);
                    _menUnit.Add(unit);
                }
            EnsureSlots(_men.Count);

            var mouse = Mouse.current;
            if (mouse != null)
            {
                // fallback for a scene without the picker, and for the frames it stands
                // down; a click spent on a screen never reaches a man
                if (mouse.leftButton.wasPressedThisFrame && !_claimedThisFrame &&
                    !BookOpen && !PointerOverUi())
                    ClaimsClick(mouse.position.ReadValue());
                ReadRightClick(mouse);
            }
            _claimedThisFrame = false;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.ClaimsEsc)
                _crews.Select(null);

            float w = Screen.width, h = Screen.height;
            float scale = _canvas.scaleFactor;
            var selected = _crews.Selected;

            for (int i = 0; i < _dots.Count; i++)
            {
                var img = _dots[i];
                var tag = _tags[i];
                var glyph = _glyphs[i];
                if (i >= _men.Count || _men[i].Tf == null || _men[i].Dead || _crews.IsAboard(_men[i]))
                {
                    if (img.enabled) img.enabled = false;
                    if (tag != null && tag.enabled) tag.enabled = false;
                    if (glyph.enabled) glyph.enabled = false;
                    continue;
                }

                var man = _men[i];
                bool boss = _menBoss[i];
                bool rival = _menUnit[i].Faction != 0;
                var screen = _cam.WorldToScreenPoint(
                    man.Tf.position + Vector3.up * MarkerHeight(man));
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                if (img.enabled != on) img.enabled = on;
                bool lit = selected != null && _menUnit[i] == selected;
                // the popup names a selected lieutenant; his tag stands down under it
                bool tagOn = on && boss && tag != null && !lit;
                if (tag != null && tag.enabled != tagOn) tag.enabled = tagOn;
                if (!on) { if (glyph.enabled) glyph.enabled = false; continue; }

                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + i * 1.3f)
                    * BobAmplitude * scale;
                float size = boss ? BossSize : HoodSize;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = rival ? (boss ? RivalBoss : RivalHood) : (boss ? BossOn : HoodOn);
                img.rectTransform.localScale = Vector3.one * (lit ? SelectedScale : 1f);

                if (tagOn)
                {
                    var name = Surname(man.DisplayName);
                    if (rival) name = _menUnit[i].GangName.ToUpperInvariant() + " · " + name;
                    if (tag.text != name) tag.text = name;
                    tag.transform.position = new Vector3(
                        screen.x, screen.y + bob + (size * 0.5f + TagLift) * scale, 0f);
                    tag.color = rival ? RivalBoss : BossOn;
                }

                // what he is doing, as the sign his crew's block on the bar shows - beside
                // the dot, moving the same way, for lieutenants only (a crew's business is
                // its lieutenant's; five signs a crew would be a swarm)
                var activity = boss ? CrewGlyphs.Of(man) : CrewGlyphs.Activity.Idle;
                var sign = CrewGlyphs.SpriteOf(activity);
                bool glyphOn = sign != null;
                if (glyph.enabled != glyphOn) glyph.enabled = glyphOn;
                if (glyphOn)
                {
                    if (glyph.sprite != sign) glyph.sprite = sign;
                    CrewGlyphs.Animate(activity, Time.unscaledTime, i * 1.3f, out var nudge, out float alpha);
                    var tint = CrewGlyphs.TintOf(activity);
                    tint.a *= alpha;
                    glyph.color = tint;
                    glyph.transform.position = new Vector3(
                        screen.x + (size * 0.5f + 12f + nudge.x) * scale, screen.y + bob + nudge.y * scale, 0f);
                }
            }

            DrawCars(w, h, scale);
            UpdateMark();
            UpdatePopup(w, h, scale);
        }

        // The outfit's car: a dot over its roof - gold when a crew owns it, dim when the
        // book has given it to nobody yet - and a tag naming it and whose it is.
        void DrawCars(float w, float h, float scale)
        {
            var cars = _crews.Cars;
            while (_carDots.Count < cars.Count)
            {
                var img = DemoUi.Icon(_dotRoot, "car", DemoUi.Dot, BossSize, BossOn);
                img.enabled = false;
                _carDots.Add(img);
                TMP_Text tag = null;
                if (_popup != null)
                {
                    tag = DemoUi.Text(_dotRoot, "car tag", 11f, BossOn, TextAlignmentOptions.Bottom, display: true);
                    tag.characterSpacing = 3f;
                    tag.rectTransform.pivot = new Vector2(0.5f, 0f);
                    tag.rectTransform.sizeDelta = new Vector2(260f, 16f);
                    tag.enabled = false;
                }
                _carTags.Add(tag);
            }
            for (int i = 0; i < _carDots.Count; i++)
            {
                var img = _carDots[i];
                var tag = _carTags[i];
                if (i >= cars.Count || cars[i].Tf == null)
                {
                    if (img.enabled) img.enabled = false;
                    if (tag != null && tag.enabled) tag.enabled = false;
                    continue;
                }
                var car = cars[i];
                var screen = _cam.WorldToScreenPoint(car.Position + Vector3.up * 2.0f);
                bool on = screen.z > 0f && screen.x >= 0f && screen.x <= w && screen.y >= 0f && screen.y <= h;
                if (img.enabled != on) img.enabled = on;
                if (tag != null && tag.enabled != on) tag.enabled = on;
                if (!on) continue;
                bool owned = car.Owner != null;
                bool lit = owned && _crews.Selected == car.Owner;
                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + 2.1f) * BobAmplitude * scale;
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = owned ? BossOn : new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.8f);
                img.rectTransform.localScale = Vector3.one * (lit ? SelectedScale : 1f);
                if (tag != null)
                {
                    string name = car.DisplayName.ToUpperInvariant() + " · " +
                                  (owned ? Surname(car.Owner.Name) : "NOBODY'S");
                    if (tag.text != name) tag.text = name;
                    tag.transform.position = new Vector3(
                        screen.x, screen.y + bob + (BossSize * 0.5f + TagLift) * scale, 0f);
                    tag.color = owned ? BossOn : DemoUi.InkDim;
                }
            }
        }

        static string Surname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return string.Empty;
            int cut = fullName.LastIndexOf(' ');
            return (cut >= 0 ? fullName.Substring(cut + 1) : fullName).ToUpperInvariant();
        }

        void UpdateMark()
        {
            if (_markAge >= MarkLife)
            {
                if (_mark.enabled) _mark.enabled = false;
                return;
            }
            _markAge += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(_markAge / MarkLife);
            var screen = _cam.WorldToScreenPoint(_markWorld);
            if (screen.z <= 0f) { _mark.enabled = false; return; }
            _mark.enabled = true;
            _mark.transform.position = new Vector3(screen.x, screen.y, 0f);
            _mark.rectTransform.localScale = Vector3.one * (1f + f * 1.6f);
            var c = _markTint;
            c.a *= 1f - f;
            _mark.color = c;
        }

        void UpdatePopup(float w, float h, float scale)
        {
            var unit = _crews.Selected;
            if (_popup == null) return;
            if (unit == null || unit.Boss == null || unit.Boss.Tf == null)
            {
                if (_popup.activeSelf) _popup.SetActive(false);
                _shownTitle = _shownLine = null;
                return;
            }

            var boss = unit.Boss;
            string title = boss.DisplayName + "  ·  Lieutenant";
            int standing = unit.Standing(), size = unit.Size();
            string line = (standing == size ? size + (size == 1 ? " man" : " men")
                                            : standing + " of " + size + " standing") +
                          "  ·  " + (unit.Car != null ? unit.Car.StatusLine
                                     : unit.Boarding != null ? "Getting in the car" : boss.StatusLine);
            if (unit.Car == null && unit.Boarding == null && !boss.HasOrder && boss.Target == null && !boss.Dead)
                line += "  ·  right-click: move / attack" + (_crews.CarOf(unit) != null ? " / car" : "");
            if (_refusal.until > Time.unscaledTime) line = _refusal.text;
            if (title != _shownTitle) { _shownTitle = title; _popupTitle.text = title; }
            if (line != _shownLine) { _shownLine = line; _popupLine.text = line; }

            var screen = _cam.WorldToScreenPoint(
                boss.Tf.position + Vector3.up * MarkerHeight(boss));
            if (screen.z <= 0f)
            {
                _popup.SetActive(false);
                return;
            }
            if (!_popup.activeSelf) _popup.SetActive(true);

            float halfWidth = PopupWidth * 0.5f * scale;
            float height = PopupHeight * scale;
            _popupRect.position = new Vector3(
                Mathf.Clamp(screen.x, halfWidth, w - halfWidth),
                Mathf.Clamp(screen.y + PopupLift * scale, 0f, h - height),
                0f);
        }
    }
}
