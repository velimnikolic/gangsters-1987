using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The corner minimap: the same 1987 survey plate as the full map, printed small
    /// and nailed into the bottom right of the screen while the player is down in the
    /// street.
    ///
    /// It is the SAME DRAWING. The plan this replaces was a second renderer altogether
    /// - its own sprites, its own colours, its own idea of what a road looked like -
    /// which meant the postcard in the corner and the plate the wheel opened were two
    /// different cities, and a player who learned one had to learn the other. So this
    /// class owns no drawing of its own at all: it asks <see cref="TurfMapSurvey"/> for
    /// one local plate around the camera pivot and shows it.
    ///
    /// Street lettering remains full-map UI, but lane lines, crossings, parks and every
    /// model footprint are baked by the same survey and become readable at this local
    /// scale. The corner and the full sheet stay the same document.
    ///
    /// The local plate is drawn on a WORKER THREAD like every other, and follows the
    /// camera in measured steps rather than uploading a texture every frame. The live
    /// marks on top - the crews and the camera's own frame - are a
    /// handful of pooled UI images rather than a raster layer, because a per-frame
    /// upload of a 960 x 600 buffer to fill a postcard is most of a millisecond for
    /// nothing.
    ///
    /// The heightfield is BORROWED from the full map's survey. Sampling the island's
    /// coastline is three quarters of a million Perlin reads and both surveys want the
    /// same island; a second one would be a visible pause at scene load.
    /// </summary>
    public sealed class TurfMinimap : MonoBehaviour
    {
        /// <summary>Under the demo's own bars, and nowhere near the full plate (60):
        /// the two are never up at the same time, but the corner card is a thing ON the
        /// HUD rather than a thing instead of the city.</summary>
        const int SortingOrder = 18;

        /// <summary>The card, in canvas units. The design's 256 x 160 render texture,
        /// which is the plate's own 8:5 - anything else would letterbox the city.
        /// </summary>
        const float CardWide = 256f, CardTall = 160f;
        const float Inset = 0f, Border = 5f;
        const float ViewOverscan = 1.25f;
        const float RedrawPanShare = 0.07f;

        /// <summary>How much bigger a crew's dot is drawn here than the plate's own
        /// scale would make it. The design's own MinimapScaleBoost: a marker that is
        /// honest about its size at this scale is one pixel and invisible.</summary>
        const float DotBoost = 2.5f;

        /// <summary>The dot, in canvas units, after the boost.</summary>
        const float DotSize = 9f;

        DemoCamera _rig;
        DemoCrews _crews;
        RoadDemoBuilder _builder;
        float _viewHeight = CityViewConfig.DefaultMinimapViewHeight;

        readonly TurfMapSurvey _survey = new TurfMapSurvey();

        /// <summary>The three static layers flattened into one, because a RawImage
        /// takes one texture and the multiply the full map gets from a material has to
        /// happen in the buffer here.</summary>
        readonly TurfPlate _sheet = new TurfPlate();

        Canvas _canvas;
        RectTransform _card, _view, _sheetRect;
        Texture2D _paper;

        /// <summary>The crew markers, pooled as Images: the Image carries its own
        /// RectTransform, so one list serves both the placing and the tinting.</summary>
        readonly List<Image> _dots = new List<Image>();
        readonly RectTransform[] _frame = new RectTransform[4];

        struct DistrictTag
        {
            public TurfDistrict District;
            public RectTransform Rect;
        }

        readonly List<DistrictTag> _districtTags = new List<DistrictTag>();

        /// <summary>Baked once and shared by every marker on the card. Static state
        /// outlives Play when domain reload is off, so it is dropped at play start
        /// and released with the card - a sprite left standing keeps its texture.
        /// </summary>
        static Sprite _dotSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Release();

        /// <summary>Lets the dot's texture go. The sprite is baked again on the next
        /// ask, so a card that outlives another may simply call for it afresh.</summary>
        static void Release()
        {
            // Unity's null, not C#'s: a sprite the editor already unloaded is only
            // dropped, and one still standing takes its texture down with it.
            if (_dotSprite != null)
            {
                if (_dotSprite.texture != null)
                    Object.Destroy(_dotSprite.texture);
                Object.Destroy(_dotSprite);
            }
            _dotSprite = null;
        }

        // ------------------------------------------------------------- the survey

        const int Idle = 0, Drawing = 1, Drawn = 2;
        volatile int _state = Idle;
        System.Exception _fault;

        /// <summary>Who held what when the plate on the card was drawn.</summary>
        int _painted = -1;

        /// <summary>Whether a plate has ever been published. Until one has, the survey's
        /// projection is a default struct whose scale is zero, and every world point put
        /// through it comes back infinite - so nothing is plotted on the card at all
        /// until there is paper under it.</summary>
        bool _printed;

        public TurfMapSurvey Survey => _survey;
        public bool Printed => _printed;
        public Rect RequestedView => WantedView();

        public void Init(RoadDemoBuilder city, Transform blocks, DemoCamera camera,
            DemoCrews streetCrews, TurfMapSurvey shareHeight)
        {
            _builder = city;
            _rig = camera;
            _crews = streetCrews;
            if (city != null)
                _viewHeight = city.MinimapViewHeight;

            _survey.Prepare(city, blocks, shareHeight);
            if (!_survey.Ready)
            {
                enabled = false;
                return;
            }

            Build();
            Kick(WantedView());
        }

        void OnDestroy()
        {
            if (_paper != null)
                Destroy(_paper);
            Release();
        }

        // -------------------------------------------------------------------- card

        void Build()
        {
            var go = new GameObject("Turf Minimap Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // A card in the corner, and the paper it is printed on showing as a border
            // all the way round - the same plate colour the full map lies on.
            _card = DemoUi.NewRect("Card", go.transform);
            _card.anchorMin = _card.anchorMax = new Vector2(1f, 0f);
            _card.pivot = new Vector2(1f, 0f);
            _card.anchoredPosition = new Vector2(-Inset, Inset);
            _card.sizeDelta = new Vector2(CardWide + Border * 2f, CardTall + Border * 2f);
            LivingCity.UI.LedgerKit.Fill(_card, new Color32(240, 231, 205, 236));
            LivingCity.UI.LedgerKit.Frame(_card, 1f, new Color32(43, 36, 24, 150));

            _view = DemoUi.NewRect("View", _card);
            DemoUi.Fill(_view, Border);
            _view.gameObject.AddComponent<RectMask2D>();

            _paper = TurfPlate.NewTexture("Turf Minimap");
            // Held at a fifth of its drawn size, so a point filter would drop four
            // pixels in five and shatter every kerb hairline on the sheet. This is the
            // one place on the map where the paper is read smaller than it was printed.
            _paper.filterMode = FilterMode.Bilinear;

            _sheetRect = DemoUi.NewRect("Sheet", _view);
            _sheetRect.anchorMin = _sheetRect.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetRect.pivot = new Vector2(0.5f, 0.5f);
            var paperImage = _sheetRect.gameObject.AddComponent<RawImage>();
            paperImage.texture = _paper;
            paperImage.raycastTarget = false;

            BuildDistrictTags();

            for (int i = 0; i < _frame.Length; i++)
            {
                _frame[i] = DemoUi.NewRect("Frame", _view);
                _frame[i].anchorMin = _frame[i].anchorMax = new Vector2(0f, 0f);
                _frame[i].pivot = new Vector2(0.5f, 0.5f);
                LivingCity.UI.LedgerKit.Fill(_frame[i], new Color32(143, 33, 25, 220));
            }

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>Quarter names are live UI rather than baked type, so they stay readable
        /// on the small card while the shared plate underneath keeps their exact borders.</summary>
        void BuildDistrictTags()
        {
            for (int i = 0; i < _survey.Districts.Count; i++)
            {
                var district = _survey.Districts[i];
                if (district.TerritoryId == CoreQuarterId.None)
                    continue;

                var rect = DemoUi.NewRect("Quarter " + district.Name, _view);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(92f, 14f);

                var face = rect.gameObject.AddComponent<Image>();
                face.color = new Color32(239, 229, 200, 205);
                face.raycastTarget = false;

                var text = DemoUi.Text(rect, "Name", 7.5f, TurfInk.Red,
                    TextAlignmentOptions.Center, display: true);
                DemoUi.Fill(text.rectTransform);
                text.characterSpacing = 8f;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.text = district.Name;

                _districtTags.Add(new DistrictTag { District = district, Rect = rect });
            }
        }

        /// <summary>The crew dot, drawn once into a texture and shared by every marker
        /// on the card: a bright core inside a glow that falls away, which is the
        /// design's own CrewDot at the one size this map draws it.</summary>
        static Sprite Dot()
        {
            if (_dotSprite != null)
                return _dotSprite;

            const int N = 32, R = N / 2;
            var texture = new Texture2D(N, N, TextureFormat.RGBA32, false, false)
            {
                name = "Turf Crew Dot",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - R + 0.5f) * (x - R + 0.5f) +
                                         (y - R + 0.5f) * (y - R + 0.5f)) / R;
                    // white and opaque at the core, so a tint gives the family's ink
                    // straight; a glow round it that is the same colour going soft
                    float a = d >= 1f ? 0f
                        : d < 0.42f ? 1f
                        : Mathf.Lerp(0.7f, 0f, (d - 0.42f) / 0.58f);
                    pixels[y * N + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(a * 255f));
                }

            texture.SetPixelData(pixels, 0);
            texture.Apply(false);

            _dotSprite = Sprite.Create(texture, new Rect(0f, 0f, N, N),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            _dotSprite.hideFlags = HideFlags.DontSave;
            return _dotSprite;
        }

        // ------------------------------------------------------------------- draw

        Rect _kickView;

        /// <summary>One local plate on the thread pool. It is redrawn after meaningful
        /// camera travel, ownership changes, or a generated recipe replacement.</summary>
        void Kick(Rect view)
        {
            if (_state != Idle)
                return;

            _survey.ReadOwners();
            _painted = TurfMapHud.OwnershipStamp(_builder);
            _kickView = view;
            _fault = null;
            _state = Drawing;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _survey.Draw(_kickView);
                    _sheet.Compose(_survey.Ground, _survey.Turf, _survey.Built);
                }
                catch (System.Exception fault)
                {
                    _fault = fault;
                }
                finally
                {
                    _state = Drawn;
                }
            });
        }

        void Update()
        {
            if (_state == Drawn)
            {
                if (_fault != null)
                    Debug.LogError("[TurfMinimap] survey failed: " + _fault);
                else
                {
                    _survey.Publish();
                    _sheet.Apply(_paper);
                    _printed = true;
                }
                _state = Idle;
            }

            // The corner card stands down for anything that takes the screen: the full
            // plate the wheel opens, the book, the strategic map.
            bool want = !TurfMapHud.IsOpen &&
                        !LivingCity.UI.PersonnelAlmanac.IsOpen &&
                        !LivingCity.UI.StrategicMapHud.IsOpen;

            if (_canvas.gameObject.activeSelf != want)
                _canvas.gameObject.SetActive(want);
            if (!want || !_printed)
                return;

            if (_state == Idle)
            {
                var wanted = WantedView();
                if (_survey.RefreshGeometryIfNeeded())
                    Kick(wanted);
                else if (_painted != TurfMapHud.OwnershipStamp(_builder))
                    Kick(wanted);
                else
                {
                    var drawn = _survey.DrawnView;
                    float pan = drawn.height > 0f
                        ? (wanted.center - drawn.center).magnitude / drawn.height
                        : float.MaxValue;
                    if (pan >= RedrawPanShare)
                        Kick(wanted);
                }
            }

            FitSheet();
            DrawCrews();
            DrawDistrictTags();
            DrawFrame();
        }

        void DrawDistrictTags()
        {
            for (int i = 0; i < _districtTags.Count; i++)
            {
                var tag = _districtTags[i];
                bool on = OnCard(tag.District.World.center, out var at);
                if (tag.Rect.gameObject.activeSelf != on)
                    tag.Rect.gameObject.SetActive(on);
                if (on)
                    tag.Rect.anchoredPosition = at;
            }
        }

        /// <summary>The ground visible inside the card right now. Unlike the survey's
        /// larger redraw request, this follows the camera every frame.</summary>
        Rect VisibleView()
        {
            if (_rig == null)
                return _survey.CityView;

            float height = Mathf.Max(120f, _viewHeight);
            float width = height * TurfPlate.AW / TurfPlate.AH;
            var centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            return new Rect(centre - new Vector2(width, height) * 0.5f,
                new Vector2(width, height));
        }

        /// <summary>The worker draws beyond the card edges. That spare paper lets the
        /// published texture slide every frame while the next survey is in flight,
        /// instead of standing still and snapping when the replacement arrives.</summary>
        Rect WantedView()
        {
            var visible = VisibleView();
            var size = visible.size * ViewOverscan;
            return new Rect(visible.center - size * 0.5f, size);
        }

        void FitSheet()
        {
            var drawn = _survey.DrawnView;
            var visible = VisibleView();
            if (drawn.width <= 0f || drawn.height <= 0f ||
                visible.width <= 0f || visible.height <= 0f)
                return;

            // A script reload can add this field while the already-running card still
            // owns the old child. Adopt it in place so Play does not need restarting.
            if (_sheetRect == null && _view != null)
            {
                _sheetRect = _view.Find("Sheet") as RectTransform;
                if (_sheetRect != null)
                {
                    _sheetRect.anchorMin = _sheetRect.anchorMax = new Vector2(0.5f, 0.5f);
                    _sheetRect.pivot = new Vector2(0.5f, 0.5f);
                }
            }
            if (_sheetRect == null)
                return;

            var pixelsPerMetre = new Vector2(
                _view.rect.width / visible.width,
                _view.rect.height / visible.height);
            _sheetRect.sizeDelta = new Vector2(
                drawn.width * pixelsPerMetre.x,
                drawn.height * pixelsPerMetre.y);
            _sheetRect.anchoredPosition = Vector2.Scale(
                drawn.center - visible.center, pixelsPerMetre);
        }

        /// <summary>A world point on the local card, in the camera's current view.</summary>
        bool OnCard(Vector2 worldXZ, out Vector2 at)
        {
            var visible = VisibleView();
            var relative = worldXZ - visible.min;
            at = new Vector2(
                relative.x / visible.width * _view.rect.width,
                relative.y / visible.height * _view.rect.height);
            return relative.x >= 0f && relative.y >= 0f &&
                   relative.x <= visible.width && relative.y <= visible.height;
        }

        /// <summary>The crews, every one the same size and in its family's ink - the
        /// design's rule for the full map, and the card has no reason to break it.
        /// </summary>
        void DrawCrews()
        {
            int used = 0;
            if (_crews != null)
                foreach (var unit in _crews.Units)
                {
                    if (unit == null || unit.IsPolice || unit.Wiped)
                        continue;
                    if (!OnCard(new Vector2(unit.Position.x, unit.Position.z), out var at))
                        continue;

                    var dot = DotAt(used++);
                    dot.rectTransform.anchoredPosition = at;
                    dot.color = TurfHouses.For(unit.Faction).Ink;
                }

            for (int i = used; i < _dots.Count; i++)
                _dots[i].gameObject.SetActive(false);
        }

        Image DotAt(int index)
        {
            while (_dots.Count <= index)
            {
                var rect = DemoUi.NewRect("Crew", _view);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(DotSize * DotBoost, DotSize * DotBoost);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = Dot();
                image.raycastTarget = false;
                _dots.Add(image);
            }

            _dots[index].gameObject.SetActive(true);
            return _dots[index];
        }

        /// <summary>What the camera can actually see, as a box on the card. The one
        /// thing a minimap owes a player who is down in the street: where on the city
        /// he is standing.</summary>
        void DrawFrame()
        {
            if (_rig == null)
            {
                foreach (var side in _frame)
                    side.gameObject.SetActive(false);
                return;
            }

            // The same reading the full map makes of the boom, so the box on the card
            // and the ground the wheel would open on are the same rectangle.
            float down = Mathf.Max(20f, _rig.distance * DemoCamera.BoomToMetres);
            float across = down * Mathf.Max(0.1f,
                (float)Screen.width / Mathf.Max(1, Screen.height));

            var centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            OnCard(centre, out var at);
            OnCard(centre + new Vector2(across, down) * 0.5f, out var corner);

            float halfW = Mathf.Abs(corner.x - at.x);
            float halfH = Mathf.Abs(corner.y - at.y);

            // top, bottom, left, right - one pixel of rule, which is what the plate
            // draws every other hairline at
            Side(0, at + new Vector2(0f, halfH), halfW * 2f, 1f);
            Side(1, at - new Vector2(0f, halfH), halfW * 2f, 1f);
            Side(2, at - new Vector2(halfW, 0f), 1f, halfH * 2f);
            Side(3, at + new Vector2(halfW, 0f), 1f, halfH * 2f);
        }

        void Side(int index, Vector2 at, float wide, float tall)
        {
            var rect = _frame[index];
            rect.gameObject.SetActive(true);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(wide, tall);
        }
    }
}
