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
        const float GroundSquareHalf = 0.5f;
        const float GroundSquareLift = 0.05f;
        const float GroundSquareThickness = 2.5f;

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
        static readonly Color FriendGround = new Color(0.1f, 0.95f, 0.28f, 0.95f);
        static readonly Color SelectedGround = new Color(1f, 0.16f, 0.12f, 1f);
        static readonly Color PoliceGround = new Color(0.2f, 0.55f, 1f, 0.95f);
        static readonly Color EnemyGround = new Color(1f, 0.9f, 0.12f, 0.95f);

        /// <summary>A rival's dot in HIS FAMILY'S colour, boss solid and hoods faded -
        /// the alpha is what says rank, the hue what says whose men these are. One red
        /// for every mob was fine while there was one mob; with twenty of them on the
        /// map the player could not tell the crew he is at war with from the crew he
        /// walked past, and the map already washes their ground in these same colours
        /// (GangPalette). A faction with no colour of its own falls back to the old red
        /// rather than the palette's grey, which is what the crowd is drawn in.</summary>
        static Color RivalInk(int faction, bool boss)
        {
            if (faction <= 0 || faction >= LivingCity.UI.GangPalette.Count)
                return boss ? RivalBoss : RivalHood;
            var colour = LivingCity.UI.GangPalette.Of(faction);
            colour.a = boss ? 1f : 0.6f;
            return colour;
        }
        static readonly Color PoliceBoss = new Color(0.38f, 0.70f, 1f, 1f);
        static readonly Color PoliceHood = new Color(0.38f, 0.70f, 1f, 0.6f);
        static readonly Color AttackTint = new Color(1f, 0.36f, 0.30f, 0.9f);

        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen ||
                                TurfMapHud.IsOpen;

        DemoCrews _crews;
        Canvas _canvas;
        Camera _cam;
        Transform _dotRoot;
        readonly List<Image> _dots = new List<Image>();
        readonly List<GroundSquareGraphic> _groundSquares = new List<GroundSquareGraphic>();
        readonly List<TMP_Text> _tags = new List<TMP_Text>();
        readonly List<Image> _glyphs = new List<Image>(); // the activity sign beside a boss's dot
        readonly List<CrewWalker> _men = new List<CrewWalker>();
        readonly List<bool> _menBoss = new List<bool>();
        readonly List<DemoCrews.Unit> _menUnit = new List<DemoCrews.Unit>();
        readonly Vector3[] _groundWorld = new Vector3[4];
        readonly Vector2[] _groundLocal = new Vector2[4];

        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupLine;
        string _shownTitle, _shownLine;

        Image _mark;
        (string text, float until) _refusal;
        readonly List<Image> _carDots = new List<Image>();
        readonly List<TMP_Text> _carTags = new List<TMP_Text>();
        // tag text per marker slot, kept until the thing in the slot changes: cut and
        // upper-cased afresh it was a string or three per visible marker per frame
        readonly List<(CrewWalker man, string name, DemoCrews.Unit unit, string gang, string text)> _menTag = new();
        readonly List<(CrewCar car, bool civic, string name, DemoCrews.Unit owner, string ownerName, string text)> _carTag = new();
        // what the popup last said and everything it said it about
        (CrewWalker boss, string name, int standing, int size, string status, bool boarding, bool orderHint,
            (bool carHint, bool exitHint, bool bail, string refusal) hints) _popupKey;
        RectTransform _carHint;        // "GET OUT" / "GET IN" over a car under the pointer
        Image _carHintIcon;
        TMP_Text _carHintText;
        string _carHintShown;
        Color _markTint = MarkTint;
        Vector3 _markWorld;
        float _markAge = MarkLife;

        System.Func<Vector2, bool> _previousVeto;
        bool _claimedThisFrame;
        Vector2 _rightDown;
        float _rightDownAt;
        bool _rightPending;

        /// <summary>The last right click that became a walk order: when, and where on
        /// the screen. A second one on the same spot inside <see cref="DoubleClick"/>
        /// seconds is the crew being told to GET THERE, and that is the only thing in
        /// the town that makes a man run. Everything else - a man catching his crew up,
        /// a man closing on a fight - is a walk, quick or otherwise.</summary>
        float _lastOrderAt = -10f;
        Vector2 _lastOrderAtPx;

        const float DoubleClick = 0.4f;
        const float DoubleSlackPx = 40f;

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

            var dotRootRect = new GameObject("Dots", typeof(RectTransform))
                .GetComponent<RectTransform>();
            dotRootRect.SetParent(root.transform, false);
            dotRootRect.anchorMin = Vector2.zero;
            dotRootRect.anchorMax = Vector2.one;
            dotRootRect.offsetMin = Vector2.zero;
            dotRootRect.offsetMax = Vector2.zero;
            _dotRoot = dotRootRect;

            _mark = new GameObject("Order Mark", typeof(RectTransform)).AddComponent<Image>();
            _mark.transform.SetParent(root.transform, false);
            _mark.sprite = dot;
            _mark.raycastTarget = false;
            _mark.rectTransform.sizeDelta = new Vector2(22f, 22f);
            _mark.enabled = false;

            BuildPopup();
            BuildBanner(root.transform);
            BuildCarHint(root.transform);

            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
        }

        // ------------------------------------------------------------------ chrome

        // A line of news over the street, under the crew bar: "Shots fired", "Police
        // responding", the officer's shout. Anyone may post one (Announce); it fades.
        static string bannerText;
        static float bannerUntil;
        static Color bannerTint = Color.white;
        TMP_Text _banner;

        /// <summary>Put a line up over the street for a few seconds.</summary>
        public static void Announce(string text, float seconds, Color? tint = null)
        {
            bannerText = text;
            bannerUntil = Time.unscaledTime + seconds;
            bannerTint = tint ?? new Color(1f, 0.92f, 0.78f, 1f);
        }

        void BuildBanner(Transform root)
        {
            if (DemoUi.Dot == null) return;
            _banner = DemoUi.Text(root, "Banner", 17f, Color.white, TextAlignmentOptions.Center, display: true);
            _banner.characterSpacing = 4f;
            var rect = _banner.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(900f, 26f);
            rect.anchoredPosition = new Vector2(0f, -(_crews.BarTopInset + 84f));
            _banner.enabled = false;
        }

        void UpdateBanner()
        {
            if (_banner == null) return;
            bool on = Time.unscaledTime < bannerUntil && !string.IsNullOrEmpty(bannerText);
            if (_banner.enabled != on) _banner.enabled = on;
            if (!on) return;
            if (_banner.text != bannerText) _banner.text = bannerText;
            float left = bannerUntil - Time.unscaledTime;
            var c = bannerTint;
            c.a = Mathf.Clamp01(left / 0.8f);
            _banner.color = c;
        }

        // The hint over a car the pointer is on: what a right-click on it does for the
        // selected crew - GET OUT when they are sat in it, GET IN when it is theirs and
        // they are on the pavement. A chip with the glyph and the word, riding above
        // the car's tag.
        void BuildCarHint(Transform root)
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null) return;
            _carHint = DemoUi.NewRect("Car Hint", root);
            _carHint.sizeDelta = new Vector2(116f, 30f);
            _carHint.pivot = new Vector2(0.5f, 0f);
            var back = _carHint.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            DemoUi.Dress(back, DemoUi.Chip, 8f, DemoUi.Panel);

            _carHintIcon = DemoUi.Icon(_carHint, "glyph", DemoUi.IconBack, 16f, BossOn);
            var iconRect = _carHintIcon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(10f, 0f);

            _carHintText = DemoUi.Text(_carHint, "word", 13f, BossOn, TextAlignmentOptions.MidlineLeft, display: true);
            _carHintText.characterSpacing = 3f;
            var textRect = _carHintText.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(32f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);
            _carHint.gameObject.SetActive(false);
        }

        // What a right-click on this car would do for the selected crew, or null.
        string CarHintFor(CrewCar car)
        {
            var unit = _crews.Selected;
            if (unit == null || car == null || car.Civic) return null;
            if (unit.Car == car) return unit.Leaving ? "GETTING OUT" : "GET OUT";
            if (car.Owner != unit || unit.Car != null) return null;
            if (car.Occupant != null && car.Occupant != unit) return null;
            return unit.Boarding == car ? "GETTING IN" : "GET IN";
        }

        void UpdateCarHint(Mouse mouse, float scale)
        {
            if (_carHint == null) return;
            string hint = null;
            CrewCar car = null;
            if (mouse != null && !BookOpen && !PointerOverUi())
            {
                car = PickCarAt(mouse.position.ReadValue());
                hint = CarHintFor(car);
            }
            bool on = hint != null;
            if (_carHint.gameObject.activeSelf != on) _carHint.gameObject.SetActive(on);
            if (!on) return;
            if (hint != _carHintShown)
            {
                _carHintShown = hint;
                _carHintText.text = hint;
                _carHintIcon.sprite = hint.Contains("OUT") ? DemoUi.IconBack : DemoUi.IconArrow;
            }
            // over the car's tag, which is itself over the roof dot
            var screen = _cam.WorldToScreenPoint(car.Position + Vector3.up * 2.0f);
            if (screen.z <= 0f) { _carHint.gameObject.SetActive(false); return; }
            _carHint.position = new Vector3(screen.x, screen.y + (BossSize * 0.5f + TagLift + 22f) * scale, 0f);
        }

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

            // the block that opens over a man is a SHADE of the street, not a lid on
            // it: it multiplies with what is behind (Photoshop's multiply layer), so
            // the road, the car and the men read straight through it. Without the
            // shader in the project it falls back to the flat panel.
            var background = _popup.AddComponent<Image>();
            background.raycastTarget = false;
            var shade = DemoUi.Multiply;
            DemoUi.Dress(background, DemoUi.Box, 15f, shade != null ? DemoUi.PanelShade : DemoUi.Panel);
            if (shade != null) background.material = shade;

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
                var ground = new GameObject("ground indicator", typeof(RectTransform))
                    .AddComponent<GroundSquareGraphic>();
                ground.transform.SetParent(_dotRoot, false);
                var groundRect = ground.rectTransform;
                groundRect.anchorMin = Vector2.zero;
                groundRect.anchorMax = Vector2.one;
                groundRect.offsetMin = Vector2.zero;
                groundRect.offsetMax = Vector2.zero;
                ground.raycastTarget = false;
                ground.enabled = false;
                _groundSquares.Add(ground);

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
            // the order card is on top of everything and answers first: a row runs, a
            // click off it puts the card away, and either way the street never sees it
            if (_ordersOpen)
            {
                if (!RunOrderRow(screen)) CloseOrders();
                return true;
            }
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
                    // the selected crew clicking its own car from inside: everybody out
                    if (owner != null && owner == _crews.Selected && owner.Car == car)
                    {
                        _crews.OrderCar(car);
                        return true;
                    }
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

        void DrawGroundSquare(
            GroundSquareGraphic square, CrewWalker man, Color colour, float width, float height)
        {
            if (square == null || man == null || man.Tf == null || _cam == null)
                return;

            var centre = man.Tf.position + Vector3.up * GroundSquareLift;
            var centreScreen = _cam.WorldToScreenPoint(centre);
            if (centreScreen.z <= 0f ||
                centreScreen.x < 0f || centreScreen.x > width ||
                centreScreen.y < 0f || centreScreen.y > height)
            {
                if (square.enabled)
                    square.enabled = false;
                return;
            }

            var half = GroundSquareHalf;
            _groundWorld[0] = centre + new Vector3(-half, 0f, -half);
            _groundWorld[1] = centre + new Vector3(half, 0f, -half);
            _groundWorld[2] = centre + new Vector3(half, 0f, half);
            _groundWorld[3] = centre + new Vector3(-half, 0f, half);

            var root = _dotRoot as RectTransform;
            if (!root)
            {
                if (square.enabled)
                    square.enabled = false;
                return;
            }

            for (var n = 0; n < _groundWorld.Length; n++)
            {
                var screen = _cam.WorldToScreenPoint(_groundWorld[n]);
                if (screen.z <= 0f ||
                    !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        root, screen, null, out _groundLocal[n]))
                {
                    if (square.enabled)
                        square.enabled = false;
                    return;
                }
            }

            square.Set(_groundLocal, GroundSquareThickness, colour);
            if (!square.enabled)
                square.enabled = true;
        }

        static Color GroundColour(DemoCrews.Unit unit, bool selected)
        {
            if (selected)
                return SelectedGround;
            if (unit != null && unit.IsPolice)
                return PoliceGround;
            if (unit != null && unit.Faction != 0)
                return EnemyGround;
            return FriendGround;
        }

        /// <summary>The rival family's premises under the pointer: the nearest building
        /// the ray strikes that is somebody's front. Its own raycast rather than the
        /// front card's veto chain (BuildingCardPicker) - this is the right-click order
        /// flow, which owns its own picks the way PickAt and PickCarAt do.</summary>
        GangFront FrontAt(Vector2 screen)
        {
            if (_cam == null || GangFront.All.Count == 0) return null;
            var hits = Physics.RaycastAll(_cam.ScreenPointToRay(screen), 3000f);
            if (hits.Length == 0) return null;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null) continue;
                // an order cannot be given to premises the camera is seeing through
                if (StreetCutaway.Invisible(hits[i].collider)) continue;
                var f = GangFront.Of(hits[i].collider.transform);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>The outfit's car under the pointer: a click anywhere on the body -
        /// the pointer's ray through the car's footprint at bonnet height, with a
        /// little air round it - or within the same slack as a man of its middle. The
        /// whole car, not just its middle: a click on the boot of the car the crew is
        /// sat in is an order to get out, not an order to move the car a yard.</summary>
        CrewCar PickCarAt(Vector2 screen)
        {
            if (_cam == null) return null;
            float radius = (PickRadius + 12f) * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            CrewCar best = null;
            var ray = _cam.ScreenPointToRay(screen);
            foreach (var car in _crews.Cars)
            {
                if (car.Tf == null) continue;
                var p = _cam.WorldToScreenPoint(car.Position + Vector3.up * 0.9f);
                if (p.z <= 0f) continue;
                float d = ((Vector2)p - screen).sqrMagnitude;
                // on the body itself: as good as a click dead on its middle
                var bonnet = new Plane(Vector3.up, new Vector3(0f, car.RoadY + 0.8f, 0f));
                if (bonnet.Raycast(ray, out float enter))
                {
                    var local = car.Tf.InverseTransformPoint(ray.GetPoint(enter));
                    if (Mathf.Abs(local.x) <= car.HalfWidth + 0.4f && Mathf.Abs(local.z) <= car.HalfLength + 0.4f)
                        d = 0f;
                }
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
                // the card is a question; a right click anywhere is the answer "none of
                // them", and never also an order to the ground under it
                if (_ordersOpen) { CloseOrders(); _rightPending = false; return; }
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

            // the car under the click. This crew's own: get in (or out). Somebody
            // else's - a rival's - and there is nothing to board, but there is a charge
            // to lay under it, so the card opens with that one row.
            var car = PickCarAt(up);
            if (car != null)
            {
                if (car.Owner == _crews.Selected)
                {
                    if (_crews.OrderCar(car))
                        ShowMark(car.Position + Vector3.up * 1.0f, MarkTint);
                    else if (_crews.CarRefusal != null)
                        _refusal = (_crews.CarRefusal, Time.unscaledTime + 2.5f);
                }
                // any OTHER car of the outfit's: the keys change hands. The boss is
                // pointing at a lieutenant and at a car, which is the whole order -
                // it need not matter whose it was, and it does not.
                else if (_crews.OnTheBooks(car))
                {
                    if (_crews.AssignCar(car))
                        ShowMark(car.Position + Vector3.up * 1.0f, MarkTint);
                    else if (_crews.CarRefusal != null)
                        _refusal = (_crews.CarRefusal, Time.unscaledTime + 2.5f);
                }
                else OpenCarOrders(car, up);
                return;
            }

            // a rival's man under the click: the orders that can be given against his
            // crew, on a card at the pointer. It used to be the attack itself, one
            // click - there was only ever one thing to do to a rival - and there are
            // two now (the machine the ledger sold the crew is the second), so the
            // choice has to be somewhere. A card is where.
            var picked = PickAt(up);
            if (picked != null && picked.Faction != 0)
            {
                OpenOrders(picked, up);
                return;
            }

            // a rival family's premises under the click: nothing to fight, but a
            // grenade to throw at the door. (A building in front of it blocks it, the
            // way the front card's own pick does.)
            var front = FrontAt(up);
            if (front != null)
            {
                OpenFrontOrders(front, up);
                return;
            }

            // the ground all sits on one plane; the pick lands there
            var plane = new Plane(Vector3.up, new Vector3(0f, _crews.GroundY, 0f));
            var ray = _cam.ScreenPointToRay(up);
            if (!plane.Raycast(ray, out float enter)) return;
            var world = ray.GetPoint(enter);

            // ONE CLICK IS A WALK; TWO IS A RUN. The same click twice on the same spot,
            // quickly - the way anybody hurries anything along - and the crew runs the
            // bulk of the way instead of walking it. Nothing else runs: a man is not
            // put into a jog by his own tether or by a fight, because the player did
            // not ask for it and a town full of men trotting about reads as panic.
            float slackTwice = DoubleSlackPx * (_canvas != null ? _canvas.scaleFactor : 1f);
            bool run = Time.unscaledTime - _lastOrderAt <= DoubleClick &&
                       (up - _lastOrderAtPx).sqrMagnitude <= slackTwice * slackTwice;
            _lastOrderAt = Time.unscaledTime;
            _lastOrderAtPx = up;

            if (_crews.OrderSelected(world, out var destination, run))
                ShowMark(destination, MarkTint);
        }

        // ------------------------------------------------------------------ order card

        // The card that opens over a rival when he is right-clicked: what the selected
        // crew can do to HIS crew, one row each, and nothing else on it.
        //
        // Built and hit-tested by hand, no Button and no GraphicRaycaster, because that
        // is how every clickable thing on this overlay already works (CrewBar.Contains,
        // PickAt, PickCarAt): a second raycaster in a scene is a thing that quietly eats
        // the clicks the first one was answering, and the pickers here have to agree
        // about which of them owns a click. Manual rectangles cannot disagree.

        const float CardWidth = 268f;
        const float CardRow = 34f;
        const float CardPad = 7f;

        sealed class OrderRow
        {
            public RectTransform Rect;
            public Image Face;
            public TMP_Text Label, Note;
            public System.Action Run;
        }

        RectTransform _cardRect;
        readonly List<OrderRow> _cardRows = new List<OrderRow>();
        int _cardShown;
        DemoCrews.Unit _cardTarget, _cardCrew;
        GangFront _cardFront;
        CrewCar _cardPlantCar;
        bool _ordersOpen;

        static readonly Color RowFace = new Color(0.045f, 0.095f, 0.145f, 0f);
        static readonly Color RowLit = new Color(1f, 0.78f, 0.32f, 0.16f);

        /// <summary>An order the crew cannot give: readable, plainly out of reach.</summary>
        static readonly Color RowDead = new Color(0.42f, 0.50f, 0.60f, 0.75f);

        /// <summary>Open the card over this rival. Nothing happens without a crew
        /// selected - there would be nobody to give the order to.</summary>
        void OpenOrders(DemoCrews.Unit target, Vector2 screen)
        {
            var crew = _crews.Selected;
            if (crew == null || target == null || target.Wiped) return;
            if (!BuildCard()) { if (_crews.OrderAttack(target)) ShowMark(target.Position + Vector3.up * 1.2f, AttackTint); return; }

            _cardTarget = target;
            _cardCrew = crew;
            _cardShown = 0;

            Row("KILL", "the crew goes in on him", () =>
            {
                if (_crews.OrderAttack(target))
                    ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
            });

            // The machine the ledger sold this crew. The rule for whether it can go is
            // the crews' own (DemoCrews.CanDriveBy), asked here rather than restated, so
            // the row and the order can never disagree.
            //
            // AND THE ROW STANDS EVEN WHEN IT CANNOT. Hiding it was the obvious thing
            // and it was wrong: a player who has not bought a machine, or has bought one
            // and not signed it out to a lieutenant, right-clicks a rival and sees a
            // card with one line on it and nothing anywhere to say why. ("Nemam opciju
            // moto drive by samo kill.") Faded, with the reason where the note goes, is
            // what the armory counter already does with a BUY tape it cannot honour.
            //
            // AND IT SAYS HOW MANY MACHINES ARE GOING. The order sends every machine the
            // crew holds and can crew, two men to each (DemoCrews.OrderDriveBy), so a
            // crew with two machines and four men on their feet puts both on the road
            // off one click - and the note has to say so, or the player who bought the
            // second machine has no way of knowing it went.
            var bike = _crews.BikeOf(crew);
            int machines = _crews.DriveByMachines(crew, target);
            var can = bike != null && machines > 0;
            var why = bike == null ? "no machine - buy one in the ledger and give it to him"
                    : _crews.DriveByRefusal ?? "not now";
            Row("MOTO DRIVE-BY",
                !can ? why
                    : machines == 1
                        ? "two men on the " + bike.DisplayName.ToLowerInvariant() + ", one pass"
                        : Spell(machines) + " machines, " + Spell(machines * 2) +
                          " men, one pass each",
                can ? () =>
                {
                    if (_crews.OrderDriveBy(target))
                        ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.DriveByRefusal != null)
                        _refusal = (_crews.DriveByRefusal, Time.unscaledTime + 2.5f);
                }
                : (System.Action)null,
                lit: can);

            // The grenade, if the crew is carrying one. Same rule as the drive-by row:
            // the row stands even when it cannot be thrown, faded, with the reason where
            // the note goes - a player with no grenades should see WHY, not a shorter card.
            var canBomb = _crews.CanBombThrow(crew, target.Position);
            Row("BOMBA",
                canBomb ? "lob a grenade at him - it kills all it stands over"
                        : (_crews.BombRefusal ?? "no grenades"),
                canBomb ? () =>
                {
                    if (_crews.OrderBombThrow(target))
                        ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        _refusal = (_crews.BombRefusal, Time.unscaledTime + 2.5f);
                }
                : (System.Action)null,
                lit: canBomb);

            LayoutAndShow(screen);
        }

        /// <summary>Small numbers the way the card says them. The rows are written in
        /// prose - "two men on the harley davidson" - and a digit dropped in the middle
        /// of one reads as a stat rather than a sentence.</summary>
        static string Spell(int n)
        {
            switch (n)
            {
                case 1: return "one";
                case 2: return "two";
                case 3: return "three";
                case 4: return "four";
                case 5: return "five";
                case 6: return "six";
                case 7: return "seven";
                case 8: return "eight";
                default: return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The card's rows laid out, sized, placed at the pointer and shown -
        /// the tail every opener shares.</summary>
        void LayoutAndShow(Vector2 screen)
        {
            for (int i = 0; i < _cardRows.Count; i++)
            {
                bool live = i < _cardShown;
                if (_cardRows[i].Rect.gameObject.activeSelf != live)
                    _cardRows[i].Rect.gameObject.SetActive(live);
                if (live) _cardRows[i].Rect.anchoredPosition = new Vector2(CardPad, -CardPad - i * CardRow);
            }

            float height = CardPad * 2f + _cardShown * CardRow;
            _cardRect.sizeDelta = new Vector2(CardWidth, height);
            Place(screen, height);
            _cardRect.gameObject.SetActive(true);
            _ordersOpen = true;
        }

        /// <summary>The card over a rival family's PREMISES: one thing to do to a
        /// building, throw a grenade at its door. (A building is not a crew - there is no
        /// kill, no drive-by; the men are elsewhere.)</summary>
        void OpenFrontOrders(GangFront front, Vector2 screen)
        {
            var crew = _crews.Selected;
            if (crew == null || front == null) return;
            if (!BuildCard()) { if (_crews.OrderBombFront(front)) ShowMark(front.Door + Vector3.up * 1.2f, AttackTint); return; }

            _cardTarget = null;
            _cardFront = front;
            _cardPlantCar = null;
            _cardCrew = crew;
            _cardShown = 0;

            var canBomb = _crews.CanBombThrow(crew, front.Door);
            Row("BOMBA",
                canBomb ? "throw a grenade on " + front.GangName + "'s doorstep"
                        : (_crews.BombRefusal ?? "no grenades"),
                canBomb ? () =>
                {
                    if (_crews.OrderBombFront(front))
                        ShowMark(front.Door + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        _refusal = (_crews.BombRefusal, Time.unscaledTime + 2.5f);
                }
                : (System.Action)null,
                lit: canBomb);

            LayoutAndShow(screen);
        }

        /// <summary>The card over a rival's CAR: lay a charge under it, to spring when
        /// they next drive it off.</summary>
        void OpenCarOrders(CrewCar car, Vector2 screen)
        {
            var crew = _crews.Selected;
            if (crew == null || car == null) return;
            if (!BuildCard()) { if (_crews.OrderPlantBomb(car)) ShowMark(car.Position + Vector3.up * 1.2f, AttackTint); return; }

            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = car;
            _cardCrew = crew;
            _cardShown = 0;

            var canPlant = _crews.CanBombPlant(crew, car);
            Row("PODMETNI BOMBU",
                canPlant ? "lay a charge under it - it blows when they drive off"
                         : (_crews.BombRefusal ?? "cannot lay it"),
                canPlant ? () =>
                {
                    if (_crews.OrderPlantBomb(car))
                        ShowMark(car.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        _refusal = (_crews.BombRefusal, Time.unscaledTime + 2.5f);
                }
                : (System.Action)null,
                lit: canPlant);

            // The plain answer to a car: walk up and empty into it. It needs nothing
            // bought and nothing signed out, so unlike the charge it is almost never
            // the faded row - which is the point of it being on this card at all.
            var canShoot = _crews.CanShootCar(crew, car);
            Row("IZREŠETAJ",
                canShoot ? "the crew walks up and empties into it"
                         : (_crews.ShootCarRefusal ?? "cannot shoot it"),
                canShoot ? () =>
                {
                    if (_crews.OrderShootCar(car))
                        ShowMark(car.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.ShootCarRefusal != null)
                        _refusal = (_crews.ShootCarRefusal, Time.unscaledTime + 2.5f);
                }
                : (System.Action)null,
                lit: canShoot);

            LayoutAndShow(screen);
        }

        /// <summary>At the pointer, and never off the screen: a card that opens with
        /// half its rows past the bottom edge is a card with orders on it nobody can
        /// click.</summary>
        void Place(Vector2 screen, float height)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            float w = CardWidth * scale, h = height * scale;
            float x = Mathf.Clamp(screen.x + 12f, 0f, Mathf.Max(0f, Screen.width - w));
            float y = Mathf.Clamp(screen.y - 8f, h, Screen.height);
            _cardRect.position = new Vector3(x, y, 0f);
        }

        /// <summary>One line on the card. A row with no action is a row that is THERE
        /// and cannot be taken - drawn faded, and its note says why rather than the
        /// row quietly not existing.</summary>
        void Row(string label, string note, System.Action run, bool lit = true)
        {
            while (_cardRows.Count <= _cardShown) _cardRows.Add(BuildRow());
            var row = _cardRows[_cardShown++];
            row.Label.text = label;
            row.Note.text = note;
            row.Label.color = lit ? DemoUi.Ink : RowDead;
            row.Note.color = lit ? DemoUi.InkDim : RowDead;
            row.Face.color = RowFace;
            row.Run = run;
        }

        void CloseOrders()
        {
            _ordersOpen = false;
            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardCrew = null;
            if (_cardRect != null) _cardRect.gameObject.SetActive(false);
        }

        /// <summary>A click on the card. True when the card owned it - which includes a
        /// click on the card's own margin, so a near miss on a row does not throw the
        /// card away with the order half given.</summary>
        bool RunOrderRow(Vector2 screen)
        {
            if (!_ordersOpen || _cardRect == null) return false;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screen)) return false;
            for (int i = 0; i < _cardShown; i++)
            {
                var row = _cardRows[i];
                if (!RectTransformUtility.RectangleContainsScreenPoint(row.Rect, screen)) continue;
                var run = row.Run;
                if (run == null) return true;   // a row that cannot be taken swallows the
                                                // click and leaves the card up to be read
                CloseOrders();
                run();
                return true;
            }
            return true;
        }

        /// <summary>Each frame the card is up: the row under the pointer lights, and the
        /// card puts itself away the moment it is asking about something that no longer
        /// exists - the rival is down, the crew was wiped out or another was selected.
        /// A card left open over a dead man is an order waiting to be given to nobody.</summary>
        void TickOrders()
        {
            if (!_ordersOpen) return;
            // the subject the card asks about, still standing: a rival crew not yet
            // wiped, a family's premises (a building does not die), or a car not yet
            // blown. None of them, or the crew gone or swapped, and the card puts itself
            // away - an order waiting to be given to nobody.
            bool subject = (_cardTarget != null && !_cardTarget.Wiped) ||
                           _cardFront != null ||
                           (_cardPlantCar != null && _cardPlantCar.Tf != null && !_cardPlantCar.Wrecked);
            if (!subject || _cardCrew == null || _cardCrew.Wiped || _crews.Selected != _cardCrew)
            {
                CloseOrders();
                return;
            }

            var mouse = Mouse.current;
            var at = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            for (int i = 0; i < _cardShown; i++)
            {
                var row = _cardRows[i];
                bool lit = row.Run != null && mouse != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(row.Rect, at);
                var want = lit ? RowLit : RowFace;
                if (row.Face.color != want) row.Face.color = want;
            }
        }

        /// <summary>The card's chrome, made once. False without a TMP font, which is the
        /// same condition the name tags and the hover popup stand down under - and the
        /// caller then falls back to the one-click attack rather than to nothing.</summary>
        bool BuildCard()
        {
            if (_cardRect != null) return true;
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null) return false;

            _cardRect = DemoUi.NewRect("Orders", _canvas.transform);
            _cardRect.pivot = new Vector2(0f, 1f);
            _cardRect.anchorMin = _cardRect.anchorMax = new Vector2(0f, 0f);
            _cardRect.sizeDelta = new Vector2(CardWidth, CardRow + CardPad * 2f);

            var back = _cardRect.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            DemoUi.Dress(back, DemoUi.Box, 15f, DemoUi.Panel);

            var stripe = DemoUi.Block(_cardRect, "Accent", AttackTint);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(3f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -10f);

            _cardRect.gameObject.SetActive(false);
            return true;
        }

        OrderRow BuildRow()
        {
            var rect = DemoUi.NewRect("Order", _cardRect);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(CardWidth - CardPad * 2f, CardRow);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            face.color = RowFace;

            var label = DemoUi.Text(rect, "Label", 14f, DemoUi.Ink,
                TextAlignmentOptions.MidlineLeft, display: true);
            label.characterSpacing = 2f;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-8f, -2f);

            var note = DemoUi.Text(rect, "Note", 11.5f, DemoUi.InkDim,
                TextAlignmentOptions.MidlineLeft, display: false);
            var noteRect = note.rectTransform;
            noteRect.anchorMin = new Vector2(0f, 0f);
            noteRect.anchorMax = new Vector2(1f, 0.5f);
            noteRect.offsetMin = new Vector2(10f, 2f);
            noteRect.offsetMax = new Vector2(-8f, 0f);

            return new OrderRow { Rect = rect, Face = face, Label = label, Note = note };
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

            // The strategic map raises its own top-down camera and drops the iso rig's
            // Camera.main tag, so a once-cached iso camera would hit-test orders against
            // a view no longer on the screen. Track the camera the player is actually
            // looking through: the map's while it is up (so the same right-click gives
            // the same orders from the zoomed-out map), the iso camera when it is not.
            var view = LivingCity.UI.StrategicMapHud.IsOpen
                ? LivingCity.UI.StrategicMapHud.Instance?.MapCamera
                : Camera.main;
            if (view != null) _cam = view;
            if (_cam == null) return;

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
                // the middle button: the selected crew out of its car, wherever the
                // pointer is - no aiming at the car needed
                if (mouse.middleButton.wasPressedThisFrame && !BookOpen && !PointerOverUi())
                {
                    var ride = _crews.Selected?.Car ?? _crews.Selected?.Boarding;
                    if (_crews.OrderOut() && ride != null)
                        ShowMark(ride.Position + Vector3.up * 1.0f, MarkTint);
                }
            }
            _claimedThisFrame = false;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.ClaimsEsc)
            {
                if (_ordersOpen) CloseOrders();
                else _crews.Select(null);
            }
            TickOrders();

            float w = Screen.width, h = Screen.height;
            float scale = _canvas.scaleFactor;
            var selected = _crews.Selected;

            for (int i = 0; i < _dots.Count; i++)
            {
                var img = _dots[i];
                var ground = _groundSquares[i];
                var tag = _tags[i];
                var glyph = _glyphs[i];
                if (i >= _men.Count || _men[i].Tf == null || _men[i].Dead || _crews.IsAboard(_men[i]))
                {
                    if (ground.enabled) ground.enabled = false;
                    if (img.enabled) img.enabled = false;
                    if (tag != null && tag.enabled) tag.enabled = false;
                    if (glyph.enabled) glyph.enabled = false;
                    continue;
                }

                var man = _men[i];
                bool boss = _menBoss[i];
                bool rival = _menUnit[i].Faction != 0;
                bool police = _menUnit[i].IsPolice;
                bool lit = selected != null && _menUnit[i] == selected;
                var screen = _cam.WorldToScreenPoint(
                    man.Tf.position + Vector3.up * MarkerHeight(man));
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                if (img.enabled != on) img.enabled = on;
                DrawGroundSquare(ground, man, GroundColour(_menUnit[i], lit), w, h);
                // the popup names a selected lieutenant; his tag stands down under it
                bool tagOn = on && boss && tag != null && !lit;
                if (tag != null && tag.enabled != tagOn) tag.enabled = tagOn;
                if (!on) { if (glyph.enabled) glyph.enabled = false; continue; }

                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + i * 1.3f)
                    * BobAmplitude * scale;
                float size = boss ? BossSize : HoodSize;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = police ? (boss ? PoliceBoss : PoliceHood)
                          : rival ? RivalInk(_menUnit[i].Faction, boss)
                          : (boss ? BossOn : HoodOn);
                img.rectTransform.localScale = Vector3.one * (lit ? SelectedScale : 1f);

                if (tagOn)
                {
                    var name = MenTag(i, man, _menUnit[i], rival);
                    if (tag.text != name) tag.text = name;
                    tag.transform.position = new Vector3(
                        screen.x, screen.y + bob + (size * 0.5f + TagLift) * scale, 0f);
                    tag.color = police ? PoliceBoss
                              : rival ? RivalInk(_menUnit[i].Faction, boss: true)
                              : BossOn;
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
            UpdateCarHint(mouse, scale);
            UpdateMark();
            UpdatePopup(w, h, scale);
            UpdateBanner();
        }

        string MenTag(int i, CrewWalker man, DemoCrews.Unit unit, bool rival)
        {
            while (_menTag.Count <= i) _menTag.Add(default);
            var slot = _menTag[i];
            if (slot.text != null && slot.man == man && slot.unit == unit &&
                ReferenceEquals(slot.name, man.DisplayName) && ReferenceEquals(slot.gang, unit.GangName))
                return slot.text;
            var name = Surname(man.DisplayName);
            if (rival) name = unit.GangName.ToUpperInvariant() + " · " + name;
            _menTag[i] = (man, man.DisplayName, unit, unit.GangName, name);
            return name;
        }

        string CarTag(int i, CrewCar car)
        {
            while (_carTag.Count <= i) _carTag.Add(default);
            var slot = _carTag[i];
            var owner = car.Owner;
            string ownerName = owner != null ? owner.Name : null;
            if (slot.text != null && slot.car == car && slot.civic == car.Civic && slot.owner == owner &&
                ReferenceEquals(slot.name, car.DisplayName) && ReferenceEquals(slot.ownerName, ownerName))
                return slot.text;
            string text = car.Civic ? "POLICE" : car.DisplayName.ToUpperInvariant() + " · " +
                          (owner != null ? Surname(owner.Name) : "NOBODY'S");
            _carTag[i] = (car, car.Civic, car.DisplayName, owner, ownerName, text);
            return text;
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
                img.color = car.Civic ? PoliceBoss : owned ? BossOn : new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.8f);
                img.rectTransform.localScale = Vector3.one * (lit ? SelectedScale : 1f);
                if (tag != null)
                {
                    string name = CarTag(i, car);
                    if (tag.text != name) tag.text = name;
                    tag.transform.position = new Vector3(
                        screen.x, screen.y + bob + (BossSize * 0.5f + TagLift) * scale, 0f);
                    tag.color = car.Civic ? PoliceBoss : owned ? BossOn : DemoUi.InkDim;
                }
            }
        }

        static string Surname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return string.Empty;
            int cut = fullName.LastIndexOf(' ');
            return (cut >= 0 ? fullName.Substring(cut + 1) : fullName).ToUpperInvariant();
        }

        sealed class GroundSquareGraphic : MaskableGraphic
        {
            readonly Vector2[] _corners = new Vector2[4];
            float _thickness = GroundSquareThickness;
            bool _hasGeometry;

            public void Set(Vector2[] source, float lineThickness, Color tint)
            {
                for (var i = 0; i < _corners.Length; i++)
                    _corners[i] = source[i];

                _thickness = Mathf.Max(1f, lineThickness);
                color = tint;
                _hasGeometry = true;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (!_hasGeometry)
                    return;

                for (var i = 0; i < _corners.Length; i++)
                    AddLine(vh, _corners[i], _corners[(i + 1) % _corners.Length]);
            }

            void AddLine(VertexHelper vh, Vector2 a, Vector2 b)
            {
                var delta = b - a;
                var length = delta.magnitude;
                if (length <= 0.001f)
                    return;

                var normal = new Vector2(-delta.y, delta.x) / length * (_thickness * 0.5f);
                var tint = (Color32)color;
                var start = vh.currentVertCount;
                vh.AddVert(a - normal, tint, Vector2.zero);
                vh.AddVert(a + normal, tint, Vector2.zero);
                vh.AddVert(b - normal, tint, Vector2.zero);
                vh.AddVert(b + normal, tint, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 1, start + 3);
            }
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
            // the words are cut again only when something they say has changed: the
            // line is half a dozen concatenations, and it ran every frame a crew was
            // selected
            int standing = unit.Standing(), size = unit.Size();
            var car = unit.Car;
            bool boarding = unit.Boarding != null;
            string status = car != null ? car.StatusLine : boarding ? null : boss.StatusLine;
            bool orderHint = car == null && !boarding && !boss.HasOrder && boss.Target == null && !boss.Dead;
            bool carHint = orderHint && _crews.CarOf(unit) != null;
            bool exitHint = car != null && !unit.Leaving;
            bool bail = exitHint && (car.Hot || car.State == CrewCar.Mode.DriveBy);
            string refusal = _refusal.until > Time.unscaledTime ? _refusal.text : null;
            var key = (boss, boss.DisplayName, standing, size, status, boarding, orderHint,
                       (carHint, exitHint, bail, refusal));
            if (_shownLine == null || !key.Equals(_popupKey))
            {
                _popupKey = key;
                string title = boss.DisplayName + "  ·  Lieutenant";
                string line = (standing == size ? size + (size == 1 ? " man" : " men")
                                                : standing + " of " + size + " standing") +
                              "  ·  " + (status ?? "Getting in the car");
                if (orderHint) line += "  ·  right-click: move / attack" + (carHint ? " / car" : "");
                else if (exitHint) line += bail ? "  ·  middle-click: bail out" : "  ·  middle-click: get out";
                if (refusal != null) line = refusal;
                if (title != _shownTitle) { _shownTitle = title; _popupTitle.text = title; }
                if (line != _shownLine) { _shownLine = line; _popupLine.text = line; }
            }

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
