using System.Collections.Generic;
using LivingCity.CameraRig;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>One choice against a rival crew. CrewOverlay owns the gameplay answer;
    /// the street and TurfMap only differ in how they draw and hit-test these rows.</summary>
    internal readonly struct CrewEnemyAction
    {
        public readonly string Label;
        public readonly string Note;
        public readonly System.Action Run;

        public CrewEnemyAction(string label, string note, System.Action run)
        {
            Label = label;
            Note = note;
            Run = run;
        }
    }

    // The indicators for the crews' men: equal-sized state-coloured square corners sit on
    // the ground around their feet, while hover and selection add ground brackets and status dots ride
    // above unselected men. The two clicks that command them: a left click within PickRadius of any
    // man of the outfit selects his crew (his chip on the top bar names him and words
    // what the crew is doing - the card that used to float over his head was
    // withdrawn on 2026-09-02 at the user's word, and the chip says it instead);
    // a right click - a click, not the camera's right-drag
    // orbit - sends the selected lieutenant to the point (the sidewalk nearest it
    // in the city) with his hoods after him, or, on a rival's man, sends the crew
    // at that rival. Same ScreenSpaceOverlay trick as the police overlay: a UI
    // transform.position IS screen pixels, so WorldToScreenPoint feeds it.
    //
    // Left clicks come through BuildingCardPicker's veto - chained behind the
    // police overlay's, which registered first - so a click on a man opens no
    // building card and a click on a cop selects the cop, not a crew behind him.
    public partial class CrewOverlay : MonoBehaviour
    {
        const float BossSize = 17f;    // reference pixels on the 1080p design height
        const float HoodSize = 10f;
        const float BobAmplitude = 3f;
        const float BobPeriod = 1.8f;
        const float SelectedScale = 1.45f;
        const float TagLift = 12f;     // surname over the boss dot
        const float HoverInterval = 0.1f;
        const float ReferenceBodyWidth = 0.5f;
        const float StateSquareSize = ReferenceBodyWidth * 1.8f;
        const float StateSquareHalfMetres = StateSquareSize * 0.5f;
        const float StateSquareGroundLift = 0.05f;
        const float StateSquareLineWidth = 0.045f;
        const float StateSquareCornerArm = StateSquareSize * 0.24f;
        const float SelectedCornerPulsePeriod = 1.4f;

        const float PickRadius = 30f;

        /// <summary>How long a refused order's reason stands on the crew's chip.</summary>
        const float RefusalSeconds = 2.5f;

        const float ClickSlackPx = 8f; // a right-drag beyond this is the camera's
        const float ClickHold = 0.45f;

        static readonly Color BossOn = new Color(1f, 0.78f, 0.32f, 1f);
        static readonly Color HoodOn = new Color(1f, 0.78f, 0.32f, 0.6f);
        static readonly Color MarkTint = new Color(1f, 0.78f, 0.32f, 0.9f);
        static readonly Color RivalBoss = new Color(1f, 0.36f, 0.30f, 1f);
        static readonly Color RivalHood = new Color(1f, 0.36f, 0.30f, 0.6f);
        static readonly Color OwnGround = new Color(0.92f, 0.72f, 0.20f, 0.86f);
        static readonly Color SelectedGround = new Color(0.24f, 0.82f, 0.34f, 0.90f);
        static readonly Color PoliceGround = new Color(0.27f, 0.59f, 0.82f, 0.82f);
        static readonly Color EnemyGround = new Color(0.84f, 0.28f, 0.24f, 0.86f);
        static readonly Color GroundShadow = new Color(0.02f, 0.025f, 0.03f, 0.22f);
        static readonly Vector3 GroundShadowOffset = new Vector3(0.018f, -0.014f, -0.018f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

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
        List<Image> _dots = new List<Image>();
        List<MeshRenderer> _groundSquares = new List<MeshRenderer>();
        List<MeshRenderer> _groundShadows = new List<MeshRenderer>();
        List<TMP_Text> _tags = new List<TMP_Text>();
        List<Image> _glyphs = new List<Image>(); // the activity sign beside a boss's dot
        List<GroundBracketGraphic> _brackets = new List<GroundBracketGraphic>();
        List<CrewWalker> _men = new List<CrewWalker>();
        List<bool> _menBoss = new List<bool>();
        List<DemoCrews.Unit> _menUnit = new List<DemoCrews.Unit>();
        MaterialPropertyBlock _groundTint;
        Mesh _groundSquareMesh;
        Mesh _selectedGroundSquareMesh;
        Vector3[] _selectedGroundVertices;
        int _selectedGroundMeshFrame = -1;
        Material _groundSquareMaterial;

        /// <summary>Whether TMP has a font to set the name tags in. Without one the
        /// dots and the ground squares still stand; the words do not.</summary>
        bool _textReady;

        List<Image> _carDots = new List<Image>();
        List<TMP_Text> _carTags = new List<TMP_Text>();
        // tag text per marker slot, kept until the thing in the slot changes: cut and
        // upper-cased afresh it was a string or three per visible marker per frame
        List<(CrewWalker man, string name, DemoCrews.Unit unit, string gang, string text)> _menTag = new();
        List<(CrewCar car, bool civic, string name, DemoCrews.Unit owner, string ownerName, string text)> _carTag = new();
        RectTransform _carHint;        // "GET OUT" / "GET IN" over a car under the pointer
        Image _carHintIcon;
        TMP_Text _carHintText;
        string _carHintShown;

        System.Func<Vector2, bool> _previousVeto;
        bool _claimedThisFrame;
        Vector2 _rightDown;
        float _rightDownAt;
        bool _rightPending;
        int _hovered = -1;
        CrewCar _hoveredCar;   // one car pick a frame, shared by the tag and the chip
        float _nextHoverAt;
        Vector3[] _bracketWorld = new Vector3[4];
        Vector2[] _bracketLocal = new Vector2[4];

        /// <summary>The last right click that became a walk order: when, and where on
        /// the screen. A second one on the same spot inside <see cref="DoubleClick"/>
        /// seconds is the crew being told to GET THERE, and that is the only thing in
        /// the town that makes a man run. Everything else - a man catching his crew up,
        /// a man closing on a fight - is a walk, quick or otherwise.</summary>
        float _lastOrderAt = -10f;
        Vector2 _lastOrderAtPx;

        // Release-to-release, so leave a little more room than the usual press
        // interval and for the tiny camera shift between the two clicks.
        const float DoubleClick = 0.55f;
        const float DoubleSlackPx = 60f;

        public void Init(DemoCrews crews)
        {
            _crews = crews;
            _groundTint = new MaterialPropertyBlock();
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
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
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

            _textReady = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
            if (!_textReady)
                Debug.LogWarning("[RoadDemo] No TMP default font - crew dots still show, " +
                                 "but the name tags are disabled.");

            BuildBanner(root.transform);
            BuildCarHint(root.transform);

            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
            if (_aiming) EndCoverAim(order: false);
            DemoCamera.RightDragTaken = false;
            if (_groundSquareMaterial != null) Destroy(_groundSquareMaterial);
            if (_groundSquareMesh != null) Destroy(_groundSquareMesh);
            if (_selectedGroundSquareMesh != null) Destroy(_selectedGroundSquareMesh);
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

        /// <summary>
        /// THE BANNER IS THE PLAYER'S NEWS, and only his. A complaint about a rival's
        /// men is a thing that happened in the city, not a thing that happened to him.
        ///
        /// The question "is this family ours?" is asked HERE and not out in the street
        /// classes on purpose (TEST-008): who the player is is a fact about identity,
        /// and the pages painted for him are the surfaces entitled to ask it. The law's
        /// dispatch used to ask the catalog itself at three call sites.
        /// </summary>
        public static void AnnounceOurs(int faction, string text, float seconds,
                                        Color? tint = null)
        {
            if (faction != LivingCity.Gangs.GangCatalog.PlayerGangId) return;
            Announce(text, seconds, tint);
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

        void UpdateCarHint(float scale)
        {
            if (_carHint == null) return;
            var car = _hoveredCar;
            var hint = CarHintFor(car);
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

        // ---------------------------------------------------------------- refusals

        // Why the last order was refused, and whose it was. The card over the man's
        // head used to print it; that card is gone (2026-09-02, the user's word) and
        // the crew's own chip on the top bar says it in red for a moment - StreetHud
        // reads these. Static like the banner beside it, and emptied when Play starts:
        // with domain reload off a crew from the last run would otherwise be held here.
        static string refusalText;
        static float refusalUntil;
        static DemoCrews.Unit refusalUnit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetRefusal()
        {
            refusalText = null;
            refusalUntil = 0f;
            refusalUnit = null;
        }

        /// <summary>What this crew was last told it could not do, while the word is
        /// still up. Null for every other crew and once it has faded.</summary>
        public static string RefusalFor(DemoCrews.Unit unit) =>
            unit != null && unit == refusalUnit && Time.unscaledTime < refusalUntil
                ? refusalText
                : null;

        /// <summary>The selected crew cannot do the thing just asked of it, and this is
        /// why. One line, in the words the system that refused used.</summary>
        void Refuse(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return;
            CrewSpeech.Say(_crews != null ? _crews.Selected : null, RefusalVoice(reason),
                LivingCity.Gameplay.CrewVoice.Priority.Refusal);
            refusalText = reason;
            refusalUntil = Time.unscaledTime + RefusalSeconds;
            refusalUnit = _crews != null ? _crews.Selected : null;
        }

        /// <summary>
        /// Which of the four refusals the men speak.
        ///
        /// The reason itself is written for the eye, one sentence per refusing system and
        /// no two the same; there is no enum behind it and inventing one would mean
        /// touching every refusal in the game to say what it already says in English. So
        /// the line is chosen off the words - and the fall-through is the generic "can't
        /// do that", which is right for anything unrecognised. The card carries the actual
        /// reason; the voice only has to carry the no.
        /// </summary>
        static string RefusalVoice(string reason)
        {
            var text = reason.ToLowerInvariant();

            if (text.Contains("nobody") || text.Contains("no man") || text.Contains("nothing left"))
                return LivingCity.Data.VoiceLines.NoMen;
            if (text.Contains("grenade") || text.Contains("machine") || text.Contains("gun") ||
                text.Contains("bomb") || text.Contains("armed"))
                return LivingCity.Data.VoiceLines.NoArms;
            if (text.Contains("too far") || text.Contains("no way") || text.Contains("cannot get") ||
                text.Contains("reach") || text.Contains("moving"))
                return LivingCity.Data.VoiceLines.NoReach;

            return LivingCity.Data.VoiceLines.NoGeneric;
        }

        // one status dot (and one tag, used only over lieutenants) per man, grown on demand
        void EnsureSlots(int count)
        {
            while (_dots.Count < count)
            {
                var groundGo = new GameObject("ground indicator", typeof(MeshFilter), typeof(MeshRenderer));
                groundGo.transform.SetParent(transform, false);
                var ground = groundGo.GetComponent<MeshRenderer>();
                groundGo.GetComponent<MeshFilter>().sharedMesh = GroundSquareMesh();
                ground.sharedMaterial = GroundSquareMaterial();
                ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ground.receiveShadows = false;
                ground.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                ground.enabled = false;
                _groundSquares.Add(ground);

                var shadowGo = new GameObject(
                    "indicator shadow", typeof(MeshFilter), typeof(MeshRenderer));
                shadowGo.transform.SetParent(groundGo.transform, false);
                shadowGo.transform.localPosition = GroundShadowOffset;
                shadowGo.GetComponent<MeshFilter>().sharedMesh = GroundSquareMesh();
                var shadow = shadowGo.GetComponent<MeshRenderer>();
                shadow.sharedMaterial = GroundSquareMaterial();
                shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadow.receiveShadows = false;
                shadow.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                shadow.sortingOrder = -1;
                shadow.enabled = false;
                _groundShadows.Add(shadow);

                var go = new GameObject("indicator", typeof(RectTransform));
                go.transform.SetParent(_dotRoot, false);
                var img = go.AddComponent<Image>();
                img.sprite = DemoUi.Dot;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(BossSize, BossSize);
                img.enabled = false;
                _dots.Add(img);

                TMP_Text tag = null;
                if (_textReady)
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

                var bracket = new GameObject("ground bracket", typeof(RectTransform))
                    .AddComponent<GroundBracketGraphic>();
                bracket.transform.SetParent(_dotRoot, false);
                bracket.raycastTarget = false;
                var rect = bracket.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                bracket.enabled = false;
                _brackets.Add(bracket);
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
            // and the same for the screen-edge paperwork: a press that worked a key on
            // the file, the wire or the ledger key must not also pick a man behind it
            if (StreetHud.Instance != null && StreetHud.Instance.Contains(screen)) return true;
            if (_previousVeto != null && _previousVeto(screen))
            {
                _crews.Select(null);
                return true;
            }
            var unit = PickAt(screen);
            if (unit == null)
            {
                var detached = PickManAt(screen, includeDetachments: true);
                if (detached >= 0 && _menUnit[detached].IsDetachment)
                {
                    _crews.Select(null);
                    return true;
                }
            }
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

            // A shop under a LEFT click is inspection only. Commands belong to the
            // right-click card; showing them here made a look at a shop double as a
            // second order surface.
            if (unit == null)
            {
                var shop = BusinessAt(screen);
                if (shop.IsValid && _door.ShowInfo(shop, screen))
                    return true;
            }
            _door.Close();

            _crews.Select(unit);
            return unit != null;
        }

        /// <summary>The shop's information over the street. The shared door panel still
        /// supplies the deed, owner and standing, but left click deliberately omits its
        /// crew and command sections.</summary>
        readonly LivingCity.UI.DoorMenu.Host _door = new LivingCity.UI.DoorMenu.Host();

        DemoCrews.Unit PickAt(Vector2 screen)
        {
            var index = PickManAt(screen);
            return index >= 0 ? _menUnit[index] : null;
        }

        int PickManAt(Vector2 screen, bool includeDetachments = false)
        {
            if (_cam == null) return -1;
            float radius = PickRadius * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            int best = -1;
            for (int i = 0; i < _men.Count; i++)
            {
                if (!includeDetachments && _menUnit[i].IsDetachment)
                    continue;
                var tf = _men[i].Tf;
                if (tf == null || _men[i].Dead || _crews.IsAboard(_men[i]) ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(tf.position))
                    continue;
                var body = _cam.WorldToScreenPoint(tf.position + Vector3.up * 0.9f);
                var dotP = _cam.WorldToScreenPoint(tf.position + Vector3.up * _men[i].OverlayHeight);
                foreach (var p in new[] { body, dotP })
                {
                    if (p.z <= 0f) continue;
                    float d = ((Vector2)p - screen).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        void DrawGroundStateSquare(
            MeshRenderer square, MeshRenderer shadow, CrewWalker man, Color colour,
            bool pulse, float width, float height)
        {
            if (square == null || man == null || man.Tf == null || _cam == null)
                return;

            var centre = man.Tf.position + Vector3.up * StateSquareGroundLift;
            var centreScreen = _cam.WorldToScreenPoint(centre);
            if (centreScreen.z <= 0f ||
                centreScreen.x < 0f || centreScreen.x > width ||
                centreScreen.y < 0f || centreScreen.y > height)
            {
                if (square.enabled)
                    square.enabled = false;
                if (shadow != null && shadow.enabled)
                    shadow.enabled = false;
                return;
            }

            // A real world-space ring: ordinary depth testing lets the body stand
            // in front of it instead of a HUD line cutting across the character.
            square.transform.SetPositionAndRotation(centre, Quaternion.identity);
            var filter = square.GetComponent<MeshFilter>();
            if (filter == null)
            {
                square.enabled = false;
                if (shadow != null)
                    shadow.enabled = false;
                return;
            }
            var wantedMesh = pulse ? SelectedGroundSquareMesh() : GroundSquareMesh();
            if (filter.sharedMesh != wantedMesh)
                filter.sharedMesh = wantedMesh;
            // Existing play-mode components can survive a script hot reload with a
            // newly added non-serialized reference left at null.
            if (_groundTint == null)
                _groundTint = new MaterialPropertyBlock();

            if (shadow != null)
            {
                var shadowFilter = shadow.GetComponent<MeshFilter>();
                if (shadowFilter != null)
                {
                    if (shadowFilter.sharedMesh != wantedMesh)
                        shadowFilter.sharedMesh = wantedMesh;
                    _groundTint.Clear();
                    _groundTint.SetColor(BaseColorId, GroundShadow);
                    _groundTint.SetColor(ColorId, GroundShadow);
                    shadow.SetPropertyBlock(_groundTint);
                    if (!shadow.enabled)
                        shadow.enabled = true;
                }
                else if (shadow.enabled)
                    shadow.enabled = false;
            }

            _groundTint.Clear();
            _groundTint.SetColor(BaseColorId, colour);
            _groundTint.SetColor(ColorId, colour);
            square.SetPropertyBlock(_groundTint);
            if (!square.enabled)
                square.enabled = true;
        }

        static Color GroundColour(DemoCrews.Unit unit, bool ownSelected)
        {
            if (unit != null && unit.IsPolice)
                return PoliceGround;
            if (unit != null && unit.Faction != 0)
                return EnemyGround;
            return ownSelected ? SelectedGround : OwnGround;
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
                if (car.Tf == null ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(car.Tf.position))
                    continue;
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
                // A PRESS ON SOMETHING TO GET BEHIND IS A QUESTION, NOT YET AN ORDER
                // (CrewOverlay.CoverAim). Held, it draws the crew's places in grey and
                // the pointer turns them; released without having moved, it is the same
                // single click it always was.
                if (_rightPending && CoverAimUnder(_rightDown, out var aim)) BeginCoverAim(aim);
                return;
            }
            // the aim owns its own release: the order leaves on the heading he stopped
            // at, and the rest of this chain never sees the click
            if (mouse.rightButton.wasReleasedThisFrame && _aiming)
            {
                _rightPending = false;
                EndCoverAim(order: true);
                return;
            }
            if (!_rightPending || !mouse.rightButton.wasReleasedThisFrame) return;
            _rightPending = false;

            var up = mouse.position.ReadValue();
            float slack = ClickSlackPx * (_canvas != null ? _canvas.scaleFactor : 1f);
            if ((up - _rightDown).sqrMagnitude > slack * slack ||
                Time.unscaledTime - _rightDownAt > ClickHold)
                return;
            if (BookOpen) return;

            // With NO crew picked, a shop under the click still answers: the card opens
            // on its crew-picker stage and comes back carrying the orders. Everything
            // else - cars, rivals, fronts, the ground - is an order TO somebody, and
            // with nobody picked there is nobody to order.
            if (_crews.Selected == null)
            {
                var shop = BusinessAt(up);
                if (shop.IsValid)
                    OpenBusinessOrders(shop, up);
                return;
            }

            var groundBag = PickBagAt(up);
            if (groundBag != null && OpenBagOrders(groundBag, up))
                return;

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
                        Refuse(_crews.CarRefusal);
                }
                // any OTHER car of the outfit's: the keys change hands. The boss is
                // pointing at a lieutenant and at a car, which is the whole order -
                // it need not matter whose it was, and it does not.
                else if (_crews.OnTheBooks(car))
                {
                    if (_crews.AssignCar(car))
                        ShowMark(car.Position + Vector3.up * 1.0f, MarkTint);
                    else if (_crews.CarRefusal != null)
                        Refuse(_crews.CarRefusal);
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

            // The autonomous bag detail can be inspected on hover, but right-clicking
            // its bodies never turns into an order to it or to the ground beneath it.
            var detachedPick = PickManAt(up, includeDetachments: true);
            if (detachedPick >= 0 && _menUnit[detachedPick].IsDetachment)
                return;

            // OUR OWN SELECTED MEN, and only when there is something to say to them that
            // a walk order cannot say (GAN-222). A right click on one's own crew is a
            // MOVE and stays one - the card opens over the selected crew only while the
            // law is on top of them or one of them is a wanted man, which is exactly when
            // RUN FOR IT and SEND HIM OUT OF TOWN are the orders the player wants and
            // walking two metres is not.
            if (picked != null && picked == _crews.Selected &&
                TryGetOwnActions(picked, _enemyActions))
            {
                OpenOwnCrewOrders(picked, up);
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

            // A WITNESS UNDER THE CLICK (GAN-245). A man whose name is on a case the
            // city has open against us is the one bystander in the city there is
            // anything to say to - so he gets a card, and everybody else on the
            // pavement goes on being ground to walk to.
            var seen = PickWitnessAt(up);
            if (seen != null)
            {
                OpenWitnessOrders(seen, up);
                return;
            }

            // a shopkeeper's premises under the click: nothing to fight and nothing to
            // blow, but everything the racket can put to him. The rows come from the one
            // shared list, so the card and the paper map cannot offer different things.
            var business = BusinessAt(up);
            if (business.IsValid)
            {
                OpenBusinessOrders(business, up);
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

            // A CLICK ON SOMETHING TO GET BEHIND IS AN AMBUSH (COVER-003). A bin, a
            // planter, a phone box - or somebody else's car stood at the kerb, which on
            // this map means the NPC parkers above all. One man takes it, the rest take
            // the furniture round it, and they lie in wait. One click walks them there,
            // two runs them, like every other order. No card: there is one thing to do
            // to a bin.
            if (DemoCrews.AnchorUnder(ray, world, out var anchor))
            {
                if (_crews.OrderAmbush(_crews.Selected, anchor))
                    ShowMark(anchor.At + Vector3.up * 0.6f, MarkTint);
                else if (_crews.AmbushRefusal != null)
                    Refuse(_crews.AmbushRefusal);
                return;
            }

            if (_crews.OrderSelected(world, out var destination, run))
                ShowMark(destination, MarkTint);
            else
                Refuse(_crews.OrderRefusal);
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

        const float CardWidth = TurfContextMenuStyle.EnemyWidth;
        const float CardRow = TurfContextMenuStyle.EnemyRowHeight;
        const float CardFoot = TurfContextMenuStyle.FooterHeight;

        sealed class OrderRow
        {
            public RectTransform Rect;
            public Image Face;
            public TMP_Text Label, Note;
            public System.Action Run;
        }

        RectTransform _cardRect;
        TMP_Text _cardTitle;
        List<OrderRow> _cardRows = new List<OrderRow>();
        List<CrewEnemyAction> _enemyActions = new List<CrewEnemyAction>();
        int _cardShown;
        DemoCrews.Unit _cardTarget, _cardCrew;
        GangFront _cardFront;
        CrewCar _cardPlantCar;
        BagOnGround _cardBag;

        /// <summary>The shop the card is asking about, when it is a shop. It is a
        /// SUBJECT like the other three and has to be held like one: the card puts
        /// itself away every frame it can name nothing it is about, and a shop that
        /// went unrecorded here made the racket card open and shut inside one frame.</summary>
        LivingCity.Territory.TerritoryBusinessId _cardBusiness;
        bool _ordersOpen;

        BagOnGround PickBagAt(Vector2 screen)
        {
            if (_cam == null)
                return null;
            var radius = PickRadius * (_canvas != null ? _canvas.scaleFactor : 1f);
            var best = radius * radius;
            BagOnGround picked = null;
            var bags = BagOnGround.All;
            for (var i = 0; i < bags.Count; i++)
            {
                var bag = bags[i];
                if (bag == null ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(
                        bag.transform.position))
                    continue;
                var point = _cam.WorldToScreenPoint(bag.transform.position + Vector3.up * 0.25f);
                if (point.z <= 0f)
                    continue;
                var d = ((Vector2)point - screen).sqrMagnitude;
                if (d >= best)
                    continue;
                best = d;
                picked = bag;
            }
            return picked;
        }

        bool OpenBagOrders(BagOnGround bag, Vector2 screen)
        {
            var crew = _crews.Selected;
            if (bag == null || crew == null || crew.Wiped || !BuildCard())
                return false;
            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBusiness = default;
            _cardBag = bag;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = "THE BAG · $" + bag.Take;
            Row("TAKE THE BAG",
                string.IsNullOrEmpty(bag.FallenName)
                    ? "$" + bag.Take + " lying in the street"
                    : "$" + bag.Take + " · fell with " + bag.FallenName,
                () =>
                {
                    if (bag != null && _crews.Selected == crew)
                        bag.Claim(_crews, crew);
                });
            LayoutAndShow(screen);
            return true;
        }

        /// <summary>Open the card over this rival. Nothing happens without a crew
        /// selected - there would be nobody to give the order to.</summary>
        void OpenOrders(DemoCrews.Unit target, Vector2 screen)
        {
            var crew = _crews.Selected;
            if (crew == null || target == null || target.Wiped) return;
            if (!BuildCard())
            {
                if (_crews.OrderAttack(target)) ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                else Refuse(_crews.OrderRefusal);
                return;
            }
            if (!TryGetEnemyActions(target, _enemyActions)) return;

            _cardTarget = target;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = target.GangName.ToUpperInvariant() + " · " +
                              target.Standing() + " MEN";

            foreach (var action in _enemyActions)
                Row(action.Label, action.Note, action.Run, action.Run != null);

            LayoutAndShow(screen);
        }

        /// <summary>The single source for what right-clicking a rival means. TurfMap asks
        /// for these same rows, so availability, refusal copy and execution cannot drift
        /// from the street card.</summary>
        internal bool TryGetEnemyActions(DemoCrews.Unit target, List<CrewEnemyAction> actions)
        {
            actions.Clear();
            var crew = _crews != null ? _crews.Selected : null;
            if (crew == null || target == null || target.Wiped)
                return false;

            actions.Add(new CrewEnemyAction("KILL", "the crew goes in on him", () =>
            {
                if (!EnemyContextCurrent(crew, target)) return;
                if (_crews.OrderAttack(target))
                    ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                else
                    Refuse(_crews.OrderRefusal);
            }));

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
            actions.Add(new CrewEnemyAction("MOTO DRIVE-BY",
                !can ? why
                    : machines == 1
                        ? "two men on the " + bike.DisplayName.ToLowerInvariant() + ", one pass"
                        : Spell(machines) + " machines, " + Spell(machines * 2) +
                          " men, one pass each",
                can ? () =>
                {
                    if (!EnemyContextCurrent(crew, target)) return;
                    if (_crews.OrderDriveBy(target))
                        ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.DriveByRefusal != null)
                        Refuse(_crews.DriveByRefusal);
                }
                : (System.Action)null));

            // The grenade, if the crew is carrying one. Same rule as the drive-by row:
            // the row stands even when it cannot be thrown, faded, with the reason where
            // the note goes - a player with no grenades should see WHY, not a shorter card.
            var canBomb = _crews.CanBombThrow(crew, target.Position);
            actions.Add(new CrewEnemyAction("BOMBA",
                canBomb ? "lob a grenade at him - it kills all it stands over"
                        : (_crews.BombRefusal ?? "no grenades"),
                canBomb ? () =>
                {
                    if (!EnemyContextCurrent(crew, target)) return;
                    if (_crews.OrderBombThrow(target))
                        ShowMark(target.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        Refuse(_crews.BombRefusal);
                }
                : (System.Action)null));

            return true;
        }

        bool EnemyContextCurrent(DemoCrews.Unit crew, DemoCrews.Unit target) =>
            _crews != null && _crews.Selected == crew && crew != null && !crew.Wiped &&
            target != null && !target.Wiped;

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
                if (live)
                    _cardRows[i].Rect.anchoredPosition = new Vector2(
                        0f, -TurfContextMenuStyle.HeaderHeight - i * CardRow);
            }

            float height = TurfContextMenuStyle.HeaderHeight + _cardShown * CardRow + CardFoot;
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

            // OUR OWN PREMISES IS NOT A TARGET. A click on the family's own door is a
            // click on the family's own house: it opens the door's ordinary menu, where
            // the one thing worth doing to it stands - taking the men inside. The
            // grenade below is for somebody else's doorstep.
            if (front.GangId == LivingCity.Gangs.GangCatalog.PlayerGangId)
            {
                // The city's own premises answer through the shared door menu. A front
                // the business directory cannot name - or one whose menu comes back
                // empty, which is a scene with no racket at all - still opens the two
                // rows the family's own house has: the men go in, the men come out.
                if (front.BusinessId.IsValid &&
                    OpenBusinessOrders(front.BusinessId, screen))
                    return;
                OpenOwnFrontOrders(front, crew, screen);
                return;
            }

            if (!BuildCard()) { if (_crews.OrderBombFront(front)) ShowMark(front.Door + Vector3.up * 1.2f, AttackTint); return; }

            _cardTarget = null;
            _cardFront = front;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = front.GangName.ToUpperInvariant() + " · PREMISES";

            var canBomb = _crews.CanBombThrow(crew, front.Door);
            Row("BOMBA",
                canBomb ? "throw a grenade on " + front.GangName + "'s doorstep"
                        : (_crews.BombRefusal ?? "no grenades"),
                canBomb ? () =>
                {
                    if (_crews.OrderBombFront(front))
                        ShowMark(front.Door + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        Refuse(_crews.BombRefusal);
                }
                : (System.Action)null,
                lit: canBomb);

            LayoutAndShow(screen);
        }

        /// <summary>
        /// The card over one of OUR OWN doors in a scene whose business directory cannot
        /// name it - the authored demo streets. The city's own premises go through the
        /// shared door menu; this is the same two rows against a door known only by the
        /// pavement outside it, so a demo scene offers the family's own house the same
        /// thing the city does.
        /// </summary>
        /// <summary>
        /// WHAT A CREW OF OURS CAN BE TOLD WHEN THE LAW IS THE PROBLEM (GAN-222).
        ///
        /// Two rows and no more: RUN FOR IT, which is the alternative to standing there
        /// and being taken, and SEND HIM OUT OF TOWN, which is what is left for a man the
        /// city will never stop looking for. Empty - and so no card at all - whenever
        /// neither applies, because a right click on one's own crew is a move order and
        /// must go on being one.
        ///
        /// The list is built here, the way the rival card's is, so the street and the
        /// paper map cannot come to offer different things.
        /// </summary>
        internal bool TryGetOwnActions(DemoCrews.Unit crew, List<CrewEnemyAction> actions)
        {
            actions.Clear();
            if (crew == null || crew.Faction != 0 || crew.Wiped || crew.Surrendered)
                return false;

            var law = NearestLaw(crew.Position);
            bool lawClose = law != null;

            if (lawClose && !crew.Fleeing)
            {
                var from = law.Position;
                actions.Add(new CrewEnemyAction("RUN FOR IT",
                    "break off and get away · they go to ground once nobody is on them",
                    () =>
                    {
                        if (!_crews.OrderFlee(crew, from)) Refuse(_crews.OrderRefusal);
                    }));
            }

            // THE OTHER END OF THE RUN. A click on the ground while they run only
            // redirects it (DemoCrews.MoveUnit), so the card is where the run is
            // called off: the flag comes down and they stand where they are, exactly
            // as a Guard order would have them.
            if (crew.Fleeing)
            {
                actions.Add(new CrewEnemyAction("STOP RUNNING",
                    "the run is over where they stand · a click on the ground only turns it",
                    () =>
                    {
                        _crews.EndFlight(crew);
                        if (!_crews.OrderUnit(crew, crew.Position, out _, run: false,
                                speak: false))
                            Refuse(_crews.OrderRefusal);
                        else
                            Announce(crew.GangName.ToUpperInvariant() + " STOP RUNNING",
                                3.5f, new Color(0.95f, 0.9f, 0.6f));
                    }));
            }

            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            if (roster != null && today > 0)
            {
                foreach (var man in crew.All())
                {
                    if (man == null || man.Dead || man.CharacterId < 0) continue;
                    var member = roster.Find(man.CharacterId);
                    if (!LivingCity.Police.WantedLevels.CanSendAway(member)) continue;
                    var away = member;
                    actions.Add(new CrewEnemyAction(
                        "SEND " + away.Surname.ToUpperInvariant() + " OUT OF TOWN",
                        LivingCity.Police.WantedLevels.OutOfTownDays +
                        " days off the board · no wage while he is gone",
                        () =>
                        {
                            if (!LivingCity.Police.WantedLevels.SendAway(away, today)) return;
                            director.Touch();
                            LivingCity.Gameplay.CrewVoice.Say(
                                LivingCity.Data.VoiceLines.OrdAway, away, crew.Position,
                                LivingCity.Gameplay.CrewVoice.Priority.Order, roster);
                            Announce(away.FullName.ToUpperInvariant() + " IS ON A BUS OUT OF THE CITY",
                                5f, new Color(0.95f, 0.9f, 0.6f));
                        }));
                }
            }

            return actions.Count > 0;
        }

        /// <summary>The nearest unit of the law on the street, or null - a police crew,
        /// which is what a beat officer's squad and a car's men both are.</summary>
        DemoCrews.Unit NearestLaw(Vector3 at)
        {
            const float near = 90f;
            DemoCrews.Unit best = null;
            float bestD = near * near;
            foreach (var unit in _crews.Units)
            {
                if (unit == null || !unit.IsPolice || unit.Wiped) continue;
                float d = (unit.Position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = unit; }
            }
            return best;
        }

        void OpenOwnCrewOrders(DemoCrews.Unit crew, Vector2 screen)
        {
            if (!BuildCard()) return;
            if (!TryGetOwnActions(crew, _enemyActions)) return;

            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = crew.GangName.ToUpperInvariant() + " · " +
                              crew.Standing() + " MEN";

            foreach (var action in _enemyActions)
                Row(action.Label, action.Note, action.Run, action.Run != null);

            LayoutAndShow(screen);
        }

        /// <summary>The witness under the pointer: the nearest bystander whose name is
        /// on a case still open against us. Its own pick, the way every other pick on
        /// this overlay is its own - PickAt walks the crews' men and a civilian is not
        /// one of them.</summary>
        CivilianAgent PickWitnessAt(Vector2 screen)
        {
            if (_cam == null || _crews == null || _crews.Selected == null) return null;
            float radius = PickRadius * (_canvas != null ? _canvas.scaleFactor : 1f);
            float bestD = radius * radius;
            CivilianAgent best = null;
            for (int i = 0; i < CivilianAgent.All.Count; i++)
            {
                var man = CivilianAgent.All[i];
                if (man == null || man.Dead || man.Tf == null) continue;
                if (WitnessWatch.NameOf(man, _crews.Selected.Faction) == null) continue;
                if (!LivingCity.Gameplay.MapVisionRegistry.IsRevealed(man.Tf.position)) continue;
                var p = _cam.WorldToScreenPoint(man.Tf.position + Vector3.up * 0.9f);
                if (p.z <= 0f) continue;
                float d = ((Vector2)p - screen).sqrMagnitude;
                if (d < bestD) { bestD = d; best = man; }
            }
            return best;
        }

        /// <summary>
        /// LEAN ON THE WITNESS. One row, because there is one thing to say to him that
        /// an attack order does not already say - the crew walks over and has a word,
        /// and either he has remembered nothing or he rings the precinct about the men
        /// who came to see him.
        ///
        /// Killing him is the ordinary attack order and is deliberately NOT on this
        /// card: it is a murder, with its own witnesses, and it must not read as a menu
        /// item beside a conversation.
        /// </summary>
        void OpenWitnessOrders(CivilianAgent body, Vector2 screen)
        {
            if (!BuildCard()) return;
            var crew = _crews.Selected;
            var witness = crew != null ? WitnessWatch.NameOf(body, crew.Faction) : null;
            if (crew == null || witness == null) return;

            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = witness.Name.ToUpperInvariant() + " · WITNESS";

            var already = WitnessWatch.Leaning(crew);
            Row("LEAN ON THE WITNESS",
                already
                    ? "they are already on their way to somebody"
                    : "walk over and have a word · he talks to the precinct if it goes wrong",
                already ? (System.Action)null : () =>
                {
                    if (body.Tf == null) return;
                    // The walk under it stays quiet: the men are not going for a stroll,
                    // they are going to have a word, and that is the line that plays.
                    if (!_crews.OrderUnit(crew, body.Tf.position, out var destination,
                            run: false, speak: false))
                    {
                        Refuse(_crews.OrderRefusal);
                        return;
                    }
                    WitnessWatch.OrderLean(crew, witness, body);
                    CrewSpeech.Say(crew, LivingCity.Data.VoiceLines.OrdWitness);
                    ShowMark(destination, AttackTint);
                },
                lit: !already);

            LayoutAndShow(screen);
        }

        void OpenOwnFrontOrders(GangFront front, DemoCrews.Unit crew, Vector2 screen)
        {
            if (!BuildCard()) return;

            _cardTarget = null;
            _cardFront = front;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            _cardTitle.text = (string.IsNullOrEmpty(front.Role) ? "OUR PREMISES"
                : front.Role.ToUpperInvariant()) + " · " +
                front.GangName.ToUpperInvariant();

            var doorstep = front.Outside;
            if (CrewQuarters.AtDoorstep(crew, doorstep))
                Row(LivingCity.Territory.TerritoryRacketOrders.MoveOutLabel,
                    "out of the door and back on the street",
                    () => CrewQuarters.BringOut(crew, speak: true), lit: true);
            else
                Row(LivingCity.Territory.TerritoryRacketOrders.MoveInLabel,
                    "the men wait inside · off the street",
                    () =>
                    {
                        if (!CrewQuarters.Station(_crews, crew, doorstep, front.Role,
                                speak: true))
                            Refuse(_crews.OrderRefusal);
                    },
                    lit: true);

            LayoutAndShow(screen);
        }

        /// <summary>
        /// The business under the pointer - THE BUILDING, not the ground near it. A shop
        /// is a thing on the street you point at, and the pavement in front of it is
        /// where the men walk: a click that lands on paving is a walk order and must
        /// never be swallowed by the shop behind it. ("Ne mogu da pomeram lika po mapi
        /// vise jer mi se uvek otvori ovaj meni iako kliknem na pavement.") So the first
        /// solid thing the ray meets decides, and it owns the click only if it IS a
        /// shop - the same rule the building card picks by.
        /// </summary>
        LivingCity.Territory.TerritoryBusinessId BusinessAt(Vector2 screen)
        {
            if (_cam == null)
                return default;

            var ray = _cam.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(
                ray, PickReach, BusinessPickMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;

                // A cutaway collider is not uniformly invisible: the current vertical
                // treatment deliberately keeps the ground-floor storefront visible. Let
                // that visible part answer, while a click through the clear upper shell
                // continues to the street behind it.
                if (StreetCutaway.Invisible(collider) && !CutawaySurfaceVisible(hit))
                    continue;
                var marker = collider
                    .GetComponentInParent<LivingCity.Entities.BusinessMarker>();
                var markerLive = marker != null && marker.isActiveAndEnabled &&
                                 marker.BusinessId.IsValid;

                // Every run uses this same physical answer. BusinessViewBindings.Count
                // is deliberately irrelevant: streamed views arrive in a different order
                // as the camera and composition budget change, and a global count must not
                // switch the whole city between a collider picker and a ground-plane picker.
                var building = markerLive ||
                               collider.GetComponentInParent<BuildingCutaway>() != null;
                if (!building)
                    return default;

                // A house that carries several shop units wears ONE marker; the hit
                // point says which unit was pointed at - the nearest authored door
                // to where the ray landed, asked at ground height.
                var territory = TerritoryRuntime.Instance;
                var businesses = LivingCity.Business.BusinessRuntime.Instance;
                var at = hit.point;
                at.y = _crews.GroundY;
                var point = new LivingCity.Territory.TerritoryPoint(at.x, at.z);

                // A marker whose own site's ground contains the hit is authoritative.
                // The nearest-door slicing below exists for ONE residential shell that
                // carries several 5 m shops; applying it first to a standalone venue
                // made a click on the gym's near corner select a neighbouring shop whose
                // door happened to be closer. DEMAND then honestly sent the crew to that
                // other door, which looked like a random march to the edge of the block.
                if (markerLive && businesses != null &&
                    businesses.TryGetSite(marker.BusinessId, out var markedSite) &&
                    markedSite.Footprint.DistanceTo(point) <= BusinessFootprintSlack)
                    return marker.BusinessId;

                if (territory != null &&
                    territory.TryGetBusinessNear(at, SliceSlack, out var sliced))
                {
                    // A marker proves the struck shell is commercial. Without one, the
                    // persistent site's own footprint must contain the hit; this recovers
                    // a not-yet-bound streamed shop without making the neighbouring house
                    // or pavement answer for it.
                    if (markerLive ||
                        (businesses != null && businesses.TryGetSite(sliced, out var site) &&
                         site.Footprint.DistanceTo(point) <= BusinessFootprintSlack))
                        return sliced;
                }
                return markerLive ? marker.BusinessId : default;
            }

            return default;
        }

        /// <summary>
        /// Whether the shell is still there to be clicked where the pointer hit it. The
        /// gradient owns the answer - the same profile the shader draws, from this
        /// building's own ground-floor line - so the picture and the click never disagree.
        /// </summary>
        bool CutawaySurfaceVisible(RaycastHit hit)
        {
            var gradient = hit.collider != null
                ? hit.collider.GetComponentInParent<BuildingOpacityGradient>()
                : null;
            if (gradient == null || !gradient.GradientMaterialsActive || _cam == null)
                return false;
            return gradient.SurfaceAlpha(hit.point, hit.normal, _cam.transform.position)
                   >= CutawayPointerAlpha;
        }

        /// <summary>How far a click reaches into the city; past the far side of it.</summary>
        const float PickReach = 3000f;

        /// <summary>Below this opacity the player is looking through the shell, not at it.</summary>
        const float CutawayPointerAlpha = 0.05f;

        /// <summary>The business picker asks for solid city geometry. Pedestrian proxies,
        /// runtime crowd capsules and small dressing props move between otherwise identical
        /// runs and must never decide whether the facade behind them is clickable.</summary>
        static readonly int BusinessPickMask =
            ~((1 << 2) | (1 << 8) | (1 << 10) |
              (1 << ScenePerf.PropLayer) | (1 << ScenePerf.CrowdLayer));

        /// <summary>How far from the ray's landing a shop unit's own door can sit and
        /// still be the unit that was pointed at - half a wide facade, no more.</summary>
        const float SliceSlack = 14f;

        /// <summary>Authored facade/collider seams may sit a fraction outside the plan's
        /// footprint; small enough that the neighbouring pavement cannot claim the shop.</summary>
        const float BusinessFootprintSlack = 0.75f;

        /// <summary>The card over a shop: what the picked crew can put to its owner - or,
        /// with nobody picked, which crew is to go.</summary>
        /// <returns>Whether a card actually opened. False for a door this scene has
        /// nothing to offer against, which is what lets a caller fall back to a card of
        /// its own - our own front in a scene with no racket behind it.</returns>
        bool OpenBusinessOrders(
            LivingCity.Territory.TerritoryBusinessId businessId, Vector2 screen)
        {
            if (!businessId.IsValid)
                return false;
            _cardScreen = screen;
            if (!TryGetRacketActions(businessId, _enemyActions) || _enemyActions.Count == 0)
                return false;
            if (!BuildCard())
                return false;

            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = businessId;
            _cardCrew = _crews.Selected;
            _cardShown = 0;

            var runtime = TerritoryRuntime.Instance;
            _cardTitle.text = runtime != null &&
                              runtime.TryGetBusinessView(businessId, out var view)
                ? view.BusinessName.ToUpperInvariant() + " · " +
                  view.Standing.ToUpperInvariant() +
                  (view.StatusLine.Length > 0
                      ? " · " + view.StatusLine.ToUpperInvariant()
                      : "") + Price(businessId)
                : businessId.Value.ToUpperInvariant();

            foreach (var action in _enemyActions)
                Row(action.Label, action.Note, action.Run, action.Run != null);

            LayoutAndShow(screen);
            return true;
        }

        /// <summary>What this owner's yes costs, beside where he stands - the same
        /// figures the ledger's block file prints, so the street and the book agree
        /// about how far off a shop is rather than both saying "wavering".</summary>
        static string Price(LivingCity.Territory.TerritoryBusinessId businessId)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !runtime.TryExplainDemand(
                    businessId,
                    new LivingCity.Territory.TerritoryGangId(
                        LivingCity.Gangs.GangCatalog.PlayerGangId),
                    out var terms))
                return "";

            return " · " + Mathf.RoundToInt(terms.Score) + " OF " +
                   Mathf.RoundToInt(terms.AcceptAt) +
                   (terms.Short <= 0f ? " · HE WOULD SAY YES" : "");
        }

        /// <summary>
        /// The single source for what right-clicking a shop means. The paper map asks for
        /// these same rows, so what is offered, what it says and what it does cannot drift
        /// between the street and the map. Every row submits through the command gateway -
        /// none of them touches the racket's state.
        /// </summary>
        internal bool TryGetRacketActions(
            LivingCity.Territory.TerritoryBusinessId businessId, List<CrewEnemyAction> actions)
        {
            actions.Clear();
            var runtime = TerritoryRuntime.Instance;
            var crew = _crews != null ? _crews.Selected : null;
            if (runtime == null || !businessId.IsValid)
                return false;

            // Nobody picked to send: the card asks that question first and answers itself
            // - pick a lieutenant and it comes straight back with what he can do. With a
            // crew already picked this stage never appears, because there is nothing to
            // ask.
            if (crew == null)
                return CrewPicker(businessId, actions);

            var gang = new LivingCity.Territory.TerritoryGangId(
                LivingCity.Gangs.GangCatalog.PlayerGangId);
            var standing = runtime.Racket != null
                ? runtime.Racket.StateOf(businessId, gang)
                : LivingCity.Territory.TerritoryProtectionState.Unaffiliated;

            var atDoor = crew != null &&
                         runtime.TryGetBusinessApproach(businessId, out var door) &&
                         runtime.HasManAt(gang, door, ApproachSlack(runtime));

            var blockOwed = 0;
            var collectionDue = runtime.Geography != null &&
                                runtime.Geography.TryGetBusinessBlock(
                                    businessId, out var collectionBlock) &&
                                runtime.TryGetCollectibleDues(collectionBlock, out blockOwed);
            LivingCity.Territory.TerritoryRacketOrders.For(
                standing,
                LivingCity.Gameplay.DoorHolder.Read(businessId),
                runtime.IsRacketable(businessId), crew != null, atDoor,
                LivingCity.Gameplay.DoorJobs.AskingPrice(businessId),
                _racketOrders,
                collectionDue,
                collectionDue
                    ? LivingCity.UI.LedgerText.Cash(blockOwed) +
                      " owed on this block · walk the round"
                    : "nothing owed yet · dues accrue daily at midnight",
                LivingCity.UI.DoorMenu.ClosureOf(businessId),
                CrewQuarters.State(crew, businessId),
                LivingCity.Territory.TerritoryHideout.Is(businessId));

            for (var i = 0; i < _racketOrders.Count; i++)
            {
                var order = _racketOrders[i];
                var word = order.Cash > 0
                    ? order.Label + " · " + LivingCity.UI.LedgerText.Cash(order.Cash)
                    : order.Label;

                System.Action run = null;
                if (order.Available)
                {
                    if (order.Kind == LivingCity.Territory.TerritoryDoorRowKind.Racket)
                    {
                        var intent = order.Intent;
                        run = () => Submit(intent, businessId);
                    }
                    else if (order.Kind ==
                             LivingCity.Territory.TerritoryDoorRowKind.Repair)
                    {
                        var repairId = businessId;
                        run = () =>
                        {
                            if (LivingCity.UI.DoorMenu.TryRead(repairId, out var door))
                                LivingCity.UI.DoorMenu.Repair(door);
                        };
                    }
                    else if (order.Kind ==
                             LivingCity.Territory.TerritoryDoorRowKind.Quarters)
                    {
                        // Men into one of our own buildings, or back out of it. The
                        // street carries it out; nothing is filed with the office.
                        var move = order.Move;
                        var housed = crew;
                        var quartersId = businessId;
                        run = () =>
                        {
                            if (move == LivingCity.Territory.TerritoryQuartersMove.Out)
                                CrewQuarters.BringOut(housed, speak: true);
                            else
                            {
                                if (!CrewQuarters.Station(_crews, housed, quartersId,
                                        speak: true))
                                    Refuse(_crews.OrderRefusal);
                            }
                        };
                    }
                    else if (order.Kind ==
                             LivingCity.Territory.TerritoryDoorRowKind.Hideout)
                    {
                        // Naming the hideout is a line in the family's book. The same act
                        // the ledger and the paper map offer, through the same call.
                        var move = order.HideoutMove;
                        var hideoutId = businessId;
                        run = () =>
                        {
                            if (LivingCity.UI.DoorMenu.TryRead(hideoutId, out var door))
                                LivingCity.UI.DoorMenu.NameHideout(door, move);
                        };
                    }
                    else
                    {
                        var type = order.Job;
                        run = () => FileDoorJob(type, businessId);
                    }
                }

                actions.Add(new CrewEnemyAction(word, order.Note, run));
            }

            return actions.Count > 0;
        }

        /// <summary>
        /// Who goes. One row per crew of the outfit that still has men on its feet; taking
        /// one picks that crew and opens the same card again, now carrying the orders.
        /// </summary>
        bool CrewPicker(
            LivingCity.Territory.TerritoryBusinessId businessId, List<CrewEnemyAction> actions)
        {
            for (var i = 0; i < _crews.Units.Count; i++)
            {
                var unit = _crews.Units[i];
                if (unit == null || unit.IsPolice || unit.Faction != 0 || unit.Wiped ||
                    unit.IsDetachment)
                    continue;

                var picked = unit;
                var name = string.IsNullOrEmpty(unit.Name)
                    ? "CREW #" + unit.CrewId
                    : unit.Name.ToUpperInvariant();
                actions.Add(new CrewEnemyAction(
                    name,
                    Spell(unit.Standing()) + " on their feet",
                    () =>
                    {
                        _crews.Select(picked);
                        OpenBusinessOrders(businessId, _cardScreen);
                    }));
            }

            if (actions.Count == 0)
                actions.Add(new CrewEnemyAction(
                    "NOBODY TO SEND", "not a man of ours left standing", null));
            return true;
        }

        Vector2 _cardScreen;

        static float ApproachSlack(TerritoryRuntime runtime) =>
            runtime.Racket != null ? runtime.Racket.Config.ApproachRadiusMetres : 14f;

        /// <summary>
        /// The order itself, through the gateway and nowhere else. Approach marches the
        /// crew; the demand and the threat are conversations, so they need a man of ours
        /// at the door and the runtime refuses them when there is none.
        /// </summary>
        void Submit(
            LivingCity.Territory.TerritoryRacketIntent intent,
            LivingCity.Territory.TerritoryBusinessId businessId)
        {
            var runtime = TerritoryRuntime.Instance;
            var crew = _crews != null ? _crews.Selected : null;
            if (runtime?.Commands == null || crew == null)
                return;

            // COLLECT is its own errand: the round takes every paying door on the
            // shop's block, not just this one (ECON-004).
            if (intent == LivingCity.Territory.TerritoryRacketIntent.Collect)
            {
                if (runtime.Geography == null ||
                    !runtime.Geography.TryGetBusinessBlock(businessId, out var roundBlock))
                    return;
                var roundResult = runtime.Commands.Submit(
                    LivingCity.Gameplay.PlayerCommands.Stamp(
                        new LivingCity.Territory.CollectDuesCommand(
                            LivingCity.Territory.TerritoryCommandNodeId.Crew(crew.CrewId),
                            roundBlock)));
                if (roundResult.Status == LivingCity.Territory.TerritoryCommandStatus.Rejected)
                {
                    if (!string.IsNullOrEmpty(roundResult.Reason))
                        Refuse(roundResult.Reason);
                }
                else
                {
                    CrewSpeech.Say(crew, LivingCity.Data.VoiceLines.RktCollect);
                    if (runtime.TryGetBusinessApproach(businessId, out var firstDoor))
                        ShowMark(firstDoor + Vector3.up * 1.0f, MarkTint);
                }
                return;
            }

            // A demand or a threat given from range is ONE order: the men walk there
            // and put it to the owner when they arrive (the approach carries the
            // intent). Only with a man already at the door is it the conversation
            // itself, spoken now.
            var gang = new LivingCity.Territory.TerritoryGangId(
                LivingCity.Gangs.GangCatalog.PlayerGangId);
            var atDoor = runtime.TryGetBusinessApproach(businessId, out var doorstep) &&
                         runtime.HasManAt(gang, doorstep, ApproachSlack(runtime));

            LivingCity.Territory.TerritoryCommandResult result;
            if (intent == LivingCity.Territory.TerritoryRacketIntent.Approach || !atDoor)
            {
                result = runtime.Commands.Submit(
                    LivingCity.Gameplay.PlayerCommands.Stamp(
                        new LivingCity.Territory.ApproachBusinessCommand(
                            LivingCity.Territory.TerritoryCommandNodeId.Crew(crew.CrewId),
                            businessId, intent)));
            }
            else
            {
                var speaker = Speaker(crew);
                result = intent == LivingCity.Territory.TerritoryRacketIntent.Demand
                    ? runtime.Commands.Submit(LivingCity.Gameplay.PlayerCommands.Stamp(
                        new LivingCity.Territory.DemandProtectionCommand(speaker, businessId)))
                    : runtime.Commands.Submit(LivingCity.Gameplay.PlayerCommands.Stamp(
                        new LivingCity.Territory.ThreatenBusinessOwnerCommand(speaker, businessId)));
            }

            if (result.Status == LivingCity.Territory.TerritoryCommandStatus.Rejected ||
                result.Status == LivingCity.Territory.TerritoryCommandStatus.Failed)
            {
                if (!string.IsNullOrEmpty(result.Reason))
                    Refuse(result.Reason);
                return;
            }

            CrewSpeech.Say(crew, intent switch
            {
                LivingCity.Territory.TerritoryRacketIntent.Demand => LivingCity.Data.VoiceLines.RktDemand,
                LivingCity.Territory.TerritoryRacketIntent.Threaten => LivingCity.Data.VoiceLines.RktThreat,
                _ => LivingCity.Data.VoiceLines.RktApproach,
            });

            if (runtime.TryGetBusinessApproach(businessId, out var door))
                ShowMark(door + Vector3.up * 1.0f,
                    intent == LivingCity.Territory.TerritoryRacketIntent.Threaten
                        ? AttackTint
                        : MarkTint);
        }

        /// <summary>
        /// Work FILED with the outfit's book rather than put to the owner on the spot -
        /// the wrecking, the torch, the robbery, the guard, the deed. The block file's
        /// keys build the very same Job through the same builder, so a shop smashed from
        /// the street and one smashed off the sheet are one order under one set of rules.
        /// </summary>
        void FileDoorJob(
            LivingCity.Outfit.OrderType type,
            LivingCity.Territory.TerritoryBusinessId businessId)
        {
            var crew = _crews != null ? _crews.Selected : null;
            if (crew == null)
                return;

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            if (outfit == null)
            {
                Refuse("the outfit's order book is not open in this scene");
                return;
            }

            if (!LivingCity.Gameplay.DoorJobs.TryBuild(
                    businessId, type, crew.CrewId, crew.Standing(),
                    out var job, out var refusal))
            {
                Refuse(refusal);
                return;
            }

            // The one purchase gate answers before the order is filed, so the street card
            // refuses a deed the safe cannot cover in the same words the ledger uses.
            if (type == LivingCity.Outfit.OrderType.BuyPremises &&
                outfit.Accounts.Safe < job.TargetWorth)
            {
                Refuse("the safe does not cover " +
                       LivingCity.UI.LedgerText.Cash(job.TargetWorth));
                return;
            }

            var result = outfit.IssueOrder(job, announce: false);
            if (!result.Ok)
            {
                Refuse(result.Reason);
                return;
            }

            // The men answer for what the men do; the deed and the paper are the desk's.
            var spoken = DoorJobVoice(type);
            if (spoken != null)
                CrewSpeech.Say(crew, spoken);
            else
                LivingCity.Gameplay.CrewVoice.Office(LivingCity.Data.VoiceLines.ForOrder(type));

            if (TerritoryRuntime.Instance != null &&
                TerritoryRuntime.Instance.TryGetBusinessApproach(businessId, out var door))
                ShowMark(door + Vector3.up * 1.0f,
                    LivingCity.Outfit.DoorOrders.IsViolence(type) ? AttackTint : MarkTint);
        }

        /// <summary>What the crew says when it is given one of the door's jobs. The deed
        /// and the repair are money and paper, so they have no street line at all - the
        /// desk answers those, through IssueOrder's own announcement.</summary>
        static string DoorJobVoice(LivingCity.Outfit.OrderType type) => type switch
        {
            LivingCity.Outfit.OrderType.SmashUp => LivingCity.Data.VoiceLines.RktSmash,
            LivingCity.Outfit.OrderType.Torch => LivingCity.Data.VoiceLines.RktTorch,
            LivingCity.Outfit.OrderType.Raid => LivingCity.Data.VoiceLines.RktRob,
            LivingCity.Outfit.OrderType.Guard => LivingCity.Data.VoiceLines.RktGuard,
            _ => null,
        };

        /// <summary>Whoever of the crew is nearest the front of it does the talking.</summary>
        static LivingCity.Territory.TerritoryCharacterId Speaker(DemoCrews.Unit crew)
        {
            foreach (var man in crew.All())
                if (man != null && !man.Dead && man.Tf != null &&
                    man.Tf.gameObject.activeInHierarchy)
                    return new LivingCity.Territory.TerritoryCharacterId(man.CharacterId);
            return default;
        }

        readonly List<LivingCity.Territory.TerritoryRacketOrder> _racketOrders =
            new List<LivingCity.Territory.TerritoryRacketOrder>();

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
            _cardBag = null;
            _cardBusiness = default;
            _cardCrew = crew;
            _cardShown = 0;
            var carOwner = car.Occupant ?? car.Owner;
            _cardTitle.text = carOwner != null
                ? carOwner.GangName.ToUpperInvariant() + " · " + car.DisplayName.ToUpperInvariant()
                : car.DisplayName.ToUpperInvariant();

            var canPlant = _crews.CanBombPlant(crew, car);
            Row("PLANT A BOMB",
                canPlant ? "lay a charge under it - it blows when they drive off"
                         : (_crews.BombRefusal ?? "cannot lay it"),
                canPlant ? () =>
                {
                    if (_crews.OrderPlantBomb(car))
                        ShowMark(car.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.BombRefusal != null)
                        Refuse(_crews.BombRefusal);
                }
                : (System.Action)null,
                lit: canPlant);

            // The plain answer to a car: walk up and empty into it. It needs nothing
            // bought and nothing signed out, so unlike the charge it is almost never
            // the faded row - which is the point of it being on this card at all.
            var canShoot = _crews.CanShootCar(crew, car);
            Row("SHOOT IT UP",
                canShoot ? "the crew walks up and empties into it"
                         : (_crews.ShootCarRefusal ?? "cannot shoot it"),
                canShoot ? () =>
                {
                    if (_crews.OrderShootCar(car))
                        ShowMark(car.Position + Vector3.up * 1.2f, AttackTint);
                    else if (_crews.ShootCarRefusal != null)
                        Refuse(_crews.ShootCarRefusal);
                }
                : (System.Action)null,
                lit: canShoot);

            // AND THE THIRD THING A CAR IS: something to get behind (COVER-005). Every
            // other stood car in the town is one right click away (DemoCrews.AnchorUnder),
            // but a rival crew's motor already opens this card, so the row lives here
            // rather than the click quietly meaning two things.
            var stood = DemoCrews.AnchorOf(car);
            bool canHide = stood.Valid && crew.Boss != null && !crew.Boss.Dead;
            Row("HIDE BEHIND IT",
                canHide ? "the crew lies in wait behind its flank"
                        : "it is moving - there is no flank to take",
                canHide ? () =>
                {
                    if (_crews.OrderAmbush(crew, stood))
                        ShowMark(stood.At + Vector3.up * 0.6f, MarkTint);
                    else if (_crews.AmbushRefusal != null)
                        Refuse(_crews.AmbushRefusal);
                }
                : (System.Action)null,
                lit: canHide);

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
            row.Label.color = lit ? TurfContextMenuStyle.Body : TurfContextMenuStyle.Disabled;
            row.Note.color = lit ? TurfContextMenuStyle.Note : TurfContextMenuStyle.Disabled;
            row.Face.color = TurfContextMenuStyle.Clear;
            row.Run = run;
        }

        void CloseOrders()
        {
            _ordersOpen = false;
            _cardTarget = null;
            _cardFront = null;
            _cardPlantCar = null;
            _cardBag = null;
            _cardBusiness = default;
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
            // wiped, a family's premises or a shopkeeper's (a building does not die), or
            // a car not yet blown. None of them and the card puts itself away.
            bool subject = (_cardTarget != null && !_cardTarget.Wiped) ||
                           _cardFront != null ||
                           _cardBusiness.IsValid ||
                           _cardBag != null ||
                           (_cardPlantCar != null && _cardPlantCar.Tf != null && !_cardPlantCar.Wrecked);

            // And somebody to ask it of. A SHOP's card is the one that is allowed to
            // stand with nobody picked, because that stage of it IS the question "who
            // goes"; every other card without a crew is an order waiting for nobody.
            bool asker = _cardBusiness.IsValid
                ? _cardCrew == null || !_cardCrew.Wiped
                : _cardCrew != null && !_cardCrew.Wiped;

            if (!subject || !asker || _crews.Selected != _cardCrew)
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
                var want = lit ? TurfContextMenuStyle.Hover : TurfContextMenuStyle.Clear;
                if (row.Face.color != want) row.Face.color = want;
                var label = row.Run == null ? TurfContextMenuStyle.Disabled
                    : lit ? TurfContextMenuStyle.Accent : TurfContextMenuStyle.Body;
                if (row.Label.color != label) row.Label.color = label;
            }
        }

        /// <summary>The card's chrome, made once. False without a TMP font, which is the
        /// same condition the name tags stand down under - and the caller then falls
        /// back to the one-click attack rather than to nothing.</summary>
        bool BuildCard()
        {
            if (_cardRect != null && _cardTitle != null) return true;
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null) return false;

            // Adopt a live pre-style card after script reload by replacing its chrome the
            // next time it is opened. Keeping the old hierarchy would leave the dark card
            // on screen until Play was restarted.
            if (_cardRect != null)
            {
                _cardRect.gameObject.SetActive(false);
                Destroy(_cardRect.gameObject);
                _cardRect = null;
                _cardRows.Clear();
                _cardShown = 0;
            }

            _cardRect = DemoUi.NewRect("Orders", _canvas.transform);
            _cardRect.pivot = new Vector2(0f, 1f);
            _cardRect.anchorMin = _cardRect.anchorMax = new Vector2(0f, 0f);
            _cardRect.sizeDelta = new Vector2(CardWidth,
                TurfContextMenuStyle.HeaderHeight + CardRow + CardFoot);

            TurfContextMenuStyle.Dress(_cardRect);
            _cardTitle = TurfContextMenuStyle.Header(_cardRect, CardWidth, string.Empty);

            _cardRect.gameObject.SetActive(false);
            return true;
        }

        OrderRow BuildRow()
        {
            var rect = DemoUi.NewRect("Order", _cardRect);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(CardWidth, CardRow);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            face.color = TurfContextMenuStyle.Clear;

            TurfContextMenuStyle.EnemyText(rect, CardWidth, out var label, out var note);

            return new OrderRow { Rect = rect, Face = face, Label = label, Note = note };
        }

        // The order mark - a gold glow pulsed onto the ground where a command landed -
        // read as a stray dot under the crew, so it is off; every call site above still
        // reports its order this way, so none of them needs to change.
        void ShowMark(Vector3 world, Color tint) { }

        // ------------------------------------------------------------------ frame

        void RemoveRuntimeChildren(Transform parent, params string[] names)
        {
            if (parent == null)
                return;

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                var remove = false;
                for (var n = 0; n < names.Length; n++)
                    if (child.name == names[n])
                    {
                        remove = true;
                        break;
                    }
                if (!remove)
                    continue;

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        void EnsureTransientCaches()
        {
            // Unity hot reload can restore only part of this code-built overlay. Restore
            // complete groups, not individual lists: their indices must remain aligned.
            var manSlotsLost = _dots == null || _groundSquares == null || _groundShadows == null ||
                               _tags == null ||
                               _glyphs == null || _brackets == null ||
                               _groundSquares.Count != _dots.Count ||
                               _groundShadows.Count != _dots.Count ||
                               _tags.Count != _dots.Count || _glyphs.Count != _dots.Count ||
                               _brackets.Count != _dots.Count;
            if (manSlotsLost)
            {
                RemoveRuntimeChildren(_dotRoot, "indicator", "tag", "glyph", "ground bracket");
                RemoveRuntimeChildren(transform, "ground indicator");
                _dots = new List<Image>();
                _groundSquares = new List<MeshRenderer>();
                _groundShadows = new List<MeshRenderer>();
                _tags = new List<TMP_Text>();
                _glyphs = new List<Image>();
                _brackets = new List<GroundBracketGraphic>();
            }

            if (_men == null || _menBoss == null || _menUnit == null)
            {
                _men = new List<CrewWalker>();
                _menBoss = new List<bool>();
                _menUnit = new List<DemoCrews.Unit>();
            }

            var carSlotsLost = _carDots == null || _carTags == null ||
                               (_carDots != null && _carTags != null &&
                                _carDots.Count != _carTags.Count);
            if (carSlotsLost)
            {
                RemoveRuntimeChildren(_dotRoot, "car", "car tag");
                _carDots = new List<Image>();
                _carTags = new List<TMP_Text>();
            }

            if (_bracketWorld == null || _bracketWorld.Length != 4)
                _bracketWorld = new Vector3[4];
            if (_bracketLocal == null || _bracketLocal.Length != 4)
                _bracketLocal = new Vector2[4];
            if (_groundTint == null)
                _groundTint = new MaterialPropertyBlock();
            if (_menTag == null)
                _menTag = new List<(CrewWalker, string, DemoCrews.Unit, string, string)>();
            if (_carTag == null)
                _carTag = new List<(CrewCar, bool, string, DemoCrews.Unit, string, string)>();
            if (_enemyActions == null)
                _enemyActions = new List<CrewEnemyAction>();

            var cardRowsLost = _cardRows == null || _cardShown < 0 ||
                               (_cardRows != null && _cardShown > _cardRows.Count);
            if (!cardRowsLost && _ordersOpen)
                for (var i = 0; i < _cardShown; i++)
                    if (_cardRows[i] == null || _cardRows[i].Rect == null)
                    {
                        cardRowsLost = true;
                        break;
                    }
            if (cardRowsLost)
            {
                _cardRows = new List<OrderRow>();
                _cardShown = 0;
                CloseOrders();
            }
        }

        void LateUpdate()
        {
            EnsureTransientCaches();
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
                // the pointer swinging under a held right button: the ambush turns
                TickCoverAim(mouse);
                // the middle button: the selected crew out of its car, wherever the
                // pointer is - no aiming at the car needed
                if (mouse.middleButton.wasPressedThisFrame && !BookOpen && !PointerOverUi())
                {
                    var ride = _crews.Selected?.Car ?? _crews.Selected?.Boarding;
                    if (_crews.OrderOut() && ride != null)
                        ShowMark(ride.Position + Vector3.up * 1.0f, MarkTint);
                }
            }

            var pointerBlocked = mouse == null || BookOpen || _ordersOpen || _aiming ||
                                 PointerOverUi();
            if (pointerBlocked)
            {
                _hovered = -1;
            }
            else if (Time.unscaledTime >= _nextHoverAt)
            {
                _nextHoverAt = Time.unscaledTime + HoverInterval;
                _hovered = PickManAt(mouse.position.ReadValue(), includeDetachments: true);
            }
            _claimedThisFrame = false;
            // The car under the pointer, asked once: the tag over its roof, the GET IN /
            // GET OUT chip and the right-click hint were three picks of the same question.
            _hoveredCar = pointerBlocked ? null : PickCarAt(mouse.position.ReadValue());

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.ClaimsEsc)
            {
                if (_aiming) EndCoverAim(order: false);
                else if (_ordersOpen) CloseOrders();
                else _crews.Select(null);
            }
            TickOrders();
            // The shop's menu is a live sheet: when the racket, the safe or a filing moves
            // under it, it is repainted where it stands.
            if (_door.Stale)
                _door.Paint();

            float w = Screen.width, h = Screen.height;
            float scale = _canvas.scaleFactor;
            var selected = _crews.Selected;

            for (int i = 0; i < _dots.Count; i++)
            {
                var img = _dots[i];
                var ground = _groundSquares[i];
                var groundShadow = _groundShadows[i];
                var tag = _tags[i];
                var glyph = _glyphs[i];
                var bracket = _brackets[i];
                if (i >= _men.Count || _men[i].Tf == null || _men[i].Dead ||
                    !_men[i].Tf.gameObject.activeInHierarchy ||
                    _crews.IsAboard(_men[i]) ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(_men[i].Tf.position))
                {
                    if (ground.enabled) ground.enabled = false;
                    if (groundShadow.enabled) groundShadow.enabled = false;
                    if (img.enabled) img.enabled = false;
                    if (tag != null && tag.enabled) tag.enabled = false;
                    if (glyph.enabled) glyph.enabled = false;
                    if (bracket.enabled) bracket.enabled = false;
                    continue;
                }

                var man = _men[i];
                bool boss = _menBoss[i];
                bool bag = _menUnit[i].IsDetachment;
                bool rival = _menUnit[i].Faction != 0;
                bool police = _menUnit[i].IsPolice;
                bool lit = selected != null && _menUnit[i] == selected;
                bool own = _menUnit[i].Faction == 0;
                bool ownSelected = lit && own && !police;
                var screen = _cam.WorldToScreenPoint(
                    man.Tf.position + Vector3.up * man.OverlayHeight);
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                DrawGroundStateSquare(
                    ground, groundShadow, man, GroundColour(_menUnit[i], ownSelected),
                    ownSelected, w, h);
                var bracketOn = on && (_hovered == i || lit) &&
                                UpdateGroundBracket(bracket, man, selected: lit, own: own);
                if (!bracketOn && bracket.enabled)
                    bracket.enabled = false;
                // The dot that used to float over every own man's head read as a stray
                // yellow circle under the crew; the ground square and the hover/select
                // bracket already say whose man he is and what state he is in, so the
                // dot stays off for good.
                var dotOn = false;
                if (img.enabled != dotOn) img.enabled = dotOn;

                // Who carries his name over his head. A tag on every lieutenant in the
                // view put four and five labels on the street at once, each of them
                // wider than the man it named and none of them anything to act on. The
                // outfit's own lieutenants keep theirs - those are the men being given
                // orders - and every other family, and the police, are named only under
                // the pointer. A selected lieutenant's name is on his card already.
                bool named = bag ? _hovered == i : own || _hovered == i;
                bool tagOn = on && (boss || bag) && tag != null && !lit && named;
                if (tag != null && tag.enabled != tagOn) tag.enabled = tagOn;
                if (!on)
                {
                    if (glyph.enabled) glyph.enabled = false;
                    continue;
                }

                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + i * 1.3f)
                    * BobAmplitude * scale;
                float size = boss ? BossSize : HoodSize;
                if (dotOn)
                {
                    img.rectTransform.sizeDelta = new Vector2(size, size);
                    img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                    img.color = police ? (boss ? PoliceBoss : PoliceHood)
                              : rival ? RivalInk(_menUnit[i].Faction, boss)
                              : (boss ? BossOn : HoodOn);
                    img.rectTransform.localScale = Vector3.one;
                }

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
                // its lieutenant's; five signs a crew would be a swarm). It follows the
                // name: the outfit's own crews wear it, a rival's is read on the hover.
                var activity = boss && named ? CrewGlyphs.Of(man) : CrewGlyphs.Activity.Idle;
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
            UpdateCarHint(scale);
            UpdateBanner();
        }

        bool UpdateGroundBracket(
            GroundBracketGraphic bracket,
            CrewWalker man,
            bool selected,
            bool own)
        {
            if (!bracket || man == null || !man.Tf)
                return false;

            if (!HumanGroundBracket.TryProject(
                    _cam, (RectTransform)_dotRoot, man.Tf,
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
                HumanGroundBracket.ArmLength(selected, selected && own, Time.unscaledTime),
                HumanGroundBracket.Thickness,
                selected ? SelectedGround : HumanGroundBracket.Tint(own));
            return true;
        }

        string MenTag(int i, CrewWalker man, DemoCrews.Unit unit, bool rival)
        {
            if (unit.IsDetachment)
            {
                var state = TerritoryRuntime.Instance != null &&
                            TerritoryRuntime.Instance.TryGetRound(
                                unit.CrewId, out _, out _, out _)
                    ? "ON THE ROUND"
                    : CrewQuarters.Inside(unit) ? "IN THE HOUSE"
                    : unit.TargetUnit != null ? "DEFENDING"
                    : "OUTSIDE";
                return "THE BAG · " + Surname(unit.Parent?.Name ?? unit.Name) +
                       " · " + state;
            }
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
            if (_crews == null || _dotRoot == null || _carDots == null || _carTags == null)
                return;
            var cars = _crews.Cars;
            // DemoCrews.Cars is itself a runtime-only readonly list; an in-place Unity
            // reload can temporarily leave it null. Men markers must keep running even
            // while that unrelated car registry is unavailable.
            if (cars == null)
                return;
            while (_carDots.Count < cars.Count)
            {
                var img = DemoUi.Icon(_dotRoot, "car", DemoUi.Dot, BossSize, BossOn);
                img.enabled = false;
                _carDots.Add(img);
                TMP_Text tag = null;
                if (_textReady)
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
                if (i >= cars.Count || cars[i].Tf == null ||
                    !LivingCity.Gameplay.MapVisionRegistry.IsRevealed(cars[i].Tf.position))
                {
                    if (img.enabled) img.enabled = false;
                    if (tag != null && tag.enabled) tag.enabled = false;
                    continue;
                }
                var car = cars[i];
                var screen = _cam.WorldToScreenPoint(car.Position + Vector3.up * 2.0f);
                bool on = LivingCity.UI.OverlayCard.OnScreen(screen, w, h);
                bool owned = car.Owner != null;
                // Same rule as the men: the outfit's own cars say what they are, a
                // rival's or a squad car is read under the pointer. "SEDAN · NOBODY'S"
                // over every kerb was a line of text the player never had to act on.
                bool tagOn = tag != null && on &&
                             ((owned && car.Owner.Faction == 0) || _hoveredCar == car);
                if (img.enabled != on) img.enabled = on;
                if (tag != null && tag.enabled != tagOn) tag.enabled = tagOn;
                if (!on) continue;
                bool lit = owned && _crews.Selected == car.Owner;
                float bob = Mathf.Sin(Time.time * (2f * Mathf.PI / BobPeriod) + 2.1f) * BobAmplitude * scale;
                img.transform.position = new Vector3(screen.x, screen.y + bob, 0f);
                img.color = car.Civic ? PoliceBoss : owned ? BossOn : new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.8f);
                img.rectTransform.localScale = Vector3.one * (lit ? SelectedScale : 1f);
                if (tagOn)
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

        static void PutGroundQuad(
            Vector3[] vertices, int quad, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var vertex = quad * 4;
            vertices[vertex] = a;
            vertices[vertex + 1] = b;
            vertices[vertex + 2] = c;
            vertices[vertex + 3] = d;
        }

        static void FillGroundCornerVertices(Vector3[] vertices, float armLength)
        {
            var half = StateSquareHalfMetres;
            var inner = Mathf.Max(0f, half - StateSquareLineWidth);
            var tip = half - Mathf.Clamp(armLength, StateSquareCornerArm, half);

            PutGroundQuad(vertices, 0,
                new Vector3(-half, 0f, inner), new Vector3(-tip, 0f, inner),
                new Vector3(-tip, 0f, half), new Vector3(-half, 0f, half));
            PutGroundQuad(vertices, 1,
                new Vector3(tip, 0f, inner), new Vector3(half, 0f, inner),
                new Vector3(half, 0f, half), new Vector3(tip, 0f, half));
            PutGroundQuad(vertices, 2,
                new Vector3(-half, 0f, -half), new Vector3(-tip, 0f, -half),
                new Vector3(-tip, 0f, -inner), new Vector3(-half, 0f, -inner));
            PutGroundQuad(vertices, 3,
                new Vector3(tip, 0f, -half), new Vector3(half, 0f, -half),
                new Vector3(half, 0f, -inner), new Vector3(tip, 0f, -inner));
            PutGroundQuad(vertices, 4,
                new Vector3(-half, 0f, -half), new Vector3(-inner, 0f, -half),
                new Vector3(-inner, 0f, -tip), new Vector3(-half, 0f, -tip));
            PutGroundQuad(vertices, 5,
                new Vector3(-half, 0f, tip), new Vector3(-inner, 0f, tip),
                new Vector3(-inner, 0f, half), new Vector3(-half, 0f, half));
            PutGroundQuad(vertices, 6,
                new Vector3(inner, 0f, -half), new Vector3(half, 0f, -half),
                new Vector3(half, 0f, -tip), new Vector3(inner, 0f, -tip));
            PutGroundQuad(vertices, 7,
                new Vector3(inner, 0f, tip), new Vector3(half, 0f, tip),
                new Vector3(half, 0f, half), new Vector3(inner, 0f, half));
        }

        static int[] GroundCornerTriangles()
        {
            var triangles = new int[96];
            for (var quad = 0; quad < 8; quad++)
            {
                var vertex = quad * 4;
                // Both windings keep the flat line visible from either side of the
                // ground plane without needing a special double-sided shader.
                var index = quad * 12;
                triangles[index] = vertex;
                triangles[index + 1] = vertex + 1;
                triangles[index + 2] = vertex + 2;
                triangles[index + 3] = vertex;
                triangles[index + 4] = vertex + 2;
                triangles[index + 5] = vertex + 3;
                triangles[index + 6] = vertex;
                triangles[index + 7] = vertex + 2;
                triangles[index + 8] = vertex + 1;
                triangles[index + 9] = vertex;
                triangles[index + 10] = vertex + 3;
                triangles[index + 11] = vertex + 2;
            }
            return triangles;
        }

        static Vector3[] GroundCornerNormals()
        {
            var normals = new Vector3[32];
            for (var i = 0; i < normals.Length; i++)
                normals[i] = Vector3.up;
            return normals;
        }

        /// <summary>A single shared set of 0.9 m square corners. It lives in the world instead of
        /// on the overlay canvas, so the normal scene depth buffer lets each character
        /// occlude the part of the marker that is behind them.</summary>
        Mesh GroundSquareMesh()
        {
            if (_groundSquareMesh != null)
                return _groundSquareMesh;

            var vertices = new Vector3[32];
            FillGroundCornerVertices(vertices, StateSquareCornerArm);
            _groundSquareMesh = new Mesh { name = "Crew State Corner Marker 0.9m" };
            _groundSquareMesh.vertices = vertices;
            _groundSquareMesh.triangles = GroundCornerTriangles();
            _groundSquareMesh.normals = GroundCornerNormals();
            _groundSquareMesh.RecalculateBounds();
            _groundSquareMesh.UploadMeshData(true);
            return _groundSquareMesh;
        }

        Mesh SelectedGroundSquareMesh()
        {
            if (_selectedGroundVertices == null || _selectedGroundVertices.Length != 32)
                _selectedGroundVertices = new Vector3[32];

            if (_selectedGroundSquareMesh == null)
            {
                FillGroundCornerVertices(_selectedGroundVertices, StateSquareCornerArm);
                _selectedGroundSquareMesh = new Mesh { name = "Selected Crew Pulsing Corners 0.9m" };
                _selectedGroundSquareMesh.MarkDynamic();
                _selectedGroundSquareMesh.vertices = _selectedGroundVertices;
                _selectedGroundSquareMesh.triangles = GroundCornerTriangles();
                _selectedGroundSquareMesh.normals = GroundCornerNormals();
                _selectedGroundSquareMesh.bounds = new Bounds(
                    Vector3.zero, new Vector3(StateSquareSize, 0.1f, StateSquareSize));
            }

            if (_selectedGroundMeshFrame != Time.frameCount)
            {
                _selectedGroundMeshFrame = Time.frameCount;
                var phase = Mathf.PingPong(
                    Time.unscaledTime * (2f / SelectedCornerPulsePeriod), 1f);
                phase = phase * phase * (3f - 2f * phase);
                var arm = Mathf.Lerp(StateSquareCornerArm, StateSquareHalfMetres, phase);
                FillGroundCornerVertices(_selectedGroundVertices, arm);
                _selectedGroundSquareMesh.vertices = _selectedGroundVertices;
            }

            return _selectedGroundSquareMesh;
        }

        Material GroundSquareMaterial()
        {
            if (_groundSquareMaterial != null)
                return _groundSquareMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Transparent") ??
                         Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            _groundSquareMaterial = new Material(shader) { name = "Crew State Marker" };
            if (_groundSquareMaterial.HasProperty(BaseColorId))
                _groundSquareMaterial.SetColor(BaseColorId, Color.white);
            if (_groundSquareMaterial.HasProperty(ColorId))
                _groundSquareMaterial.SetColor(ColorId, Color.white);
            if (_groundSquareMaterial.HasProperty("_Surface"))
                _groundSquareMaterial.SetFloat("_Surface", 1f);
            if (_groundSquareMaterial.HasProperty("_Blend"))
                _groundSquareMaterial.SetFloat("_Blend", 0f);
            if (_groundSquareMaterial.HasProperty("_ZWrite"))
                _groundSquareMaterial.SetFloat("_ZWrite", 0f);
            if (_groundSquareMaterial.HasProperty("_SrcBlend"))
                _groundSquareMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_groundSquareMaterial.HasProperty("_DstBlend"))
                _groundSquareMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _groundSquareMaterial.SetOverrideTag("RenderType", "Transparent");
            _groundSquareMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _groundSquareMaterial.renderQueue = 3000;
            _groundSquareMaterial.enableInstancing = true;
            return _groundSquareMaterial;
        }
    }
}
