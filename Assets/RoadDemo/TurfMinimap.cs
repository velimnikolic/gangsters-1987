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

        /// <summary>The card, in canvas units. The design gives the corner 290 across
        /// and lets the picture set its own height; its plate measures 528 x 344, so at
        /// 290 the map is 189 deep. Kept as the design's proportion rather than rounded
        /// to a neat ratio: the card is a WINDOW on the same city the full plate draws,
        /// and a different shape here would show a different piece of it.</summary>
        const float CardWide = 290f, CardTall = 189f;
        // No border. The card used to sit on a margin of the plate's own cream, which
        // read as a mount round a photograph; the design runs the band and the map to
        // the card's edge and rules only the side that faces the city.
        const float Inset = 0f, Border = 0f;

        /// <summary>The dark band the design puts across the head of the corner plate:
        /// where the camera is standing on the left, and who holds it on the right. A
        /// map with no caption is a picture; the band is what makes it a reading.
        /// </summary>
        const float BandTall = 18f;
        const float ViewOverscan = 1.25f;
        const float RedrawPanShare = 0.08f;
        const float RedrawZoomShare = 1.08f;
        const float RedrawAfterStillSeconds = 0.14f;
        const float MovingRedrawInterval = 0.9f;
        // The card is only 290 x 189 canvas pixels. A 480 x 300 upload was almost
        // four source texels for every displayed pixel and could make Texture2D.Apply
        // the one periodic hitch while the 3D camera was moving. The shared survey is
        // still drawn at full resolution on its worker; only the corner-card handoff is
        // reduced to 240 x 150 and bilinear filtering restores the final card size.
        const int UploadDownsample = 4;

        /// <summary>How much bigger a crew's dot is drawn here than the plate's own
        /// scale would make it. The design's own MinimapScaleBoost: a marker that is
        /// honest about its size at this scale is one pixel and invisible.</summary>
        const float DotBoost = 2.5f;

        /// <summary>The dot, in canvas units, after the boost.</summary>
        const float DotSize = 9f;

        DemoCamera _rig;
        DemoCrews _crews;
        RoadDemoBuilder _builder;
        TurfMapHud _owner;
        TurfMapBuildingLayer _buildingLayer;
        float _viewHeight = CityViewConfig.DefaultMinimapViewHeight;

        readonly TurfMapSurvey _survey = new TurfMapSurvey();

        Canvas _canvas;
        RectTransform _card, _view, _sheetPose, _sheetRect, _band;

        /// <summary>The corner plate goes dark with the rest of the street HUD: the
        /// same wash the full map lays over its own paper, and the same shared table
        /// over the card's chrome. The card is the turf map printed small, so it cannot
        /// be the one piece of paper on the screen still lit at midnight.</summary>
        readonly HudNight _night = new HudNight();
        Image _nightInk;
        TMPro.TMP_Text _bandPlace, _bandHolder;
        string _shownPlace = "", _shownHolder = "";
        Texture2D _paper;
        readonly Color32[] _uploadPixels = new Color32[
            TurfPlate.RW / UploadDownsample * (TurfPlate.RH / UploadDownsample)];
        readonly Vector3[] _clipCorners = new Vector3[4];
        Vector2 _lastPivot;
        float _lastHeading, _lastPitch;
        float _lastMotionAt = -10f, _lastKickAt = -10f;
        int _draws, _uploads;
        long _lastDrawMs;

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
        bool _paintedTurf = true, _kickTurf = true;

        /// <summary>Whether a plate has ever been published. Until one has, the survey's
        /// projection is a default struct whose scale is zero, and every world point put
        /// through it comes back infinite - so nothing is plotted on the card at all
        /// until there is paper under it.</summary>
        bool _printed;

        public TurfMapSurvey Survey => _survey;
        public bool Printed => _printed;
        public Rect RequestedView => WantedView();
        public int Draws => _draws;
        public int Uploads => _uploads;
        public long LastDrawMs => _lastDrawMs;

        public void Init(RoadDemoBuilder city, Transform blocks, DemoCamera camera,
            DemoCrews streetCrews, TurfMapSurvey shareHeight, TurfMapHud owner)
        {
            _builder = city;
            _rig = camera;
            _crews = streetCrews;
            _owner = owner;
            if (camera != null)
            {
                _lastPivot = new Vector2(camera.pivot.x, camera.pivot.z);
                _lastHeading = camera.yaw;
                _lastPitch = camera.pitch;
            }
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

            // The corner plate is inside the design's hud region too, and carries its
            // opacity with the rest of the paper.
            go.AddComponent<CanvasGroup>().alpha = HudNight.Alpha;

            // A card in the corner, and the paper it is printed on showing as a border
            // all the way round - the same plate colour the full map lies on.
            _card = DemoUi.NewRect("Card", go.transform);
            _card.anchorMin = _card.anchorMax = new Vector2(1f, 0f);
            _card.pivot = new Vector2(1f, 0f);
            _card.anchoredPosition = new Vector2(-Inset, Inset);
            _card.sizeDelta = new Vector2(CardWide + Border * 2f,
                CardTall + Border * 2f + BandTall);
            LivingCity.UI.LedgerKit.Fill(_card, LivingCity.UI.LedgerV2.Head);

            BuildBand();

            _view = DemoUi.NewRect("View", _card);
            DemoUi.Fill(_view, Border);
            // The band eats the top of the card; the plate keeps the rest.
            _view.offsetMax = new Vector2(_view.offsetMax.x, -(Border + BandTall));
            _view.gameObject.AddComponent<RectMask2D>();

            // The one rule the design keeps: down the edge that faces the city, so the
            // corner reads as a card laid on the street and not a hole cut in it.
            LivingCity.UI.LedgerKit.VRule(_card, 0f, 0f,
                CardTall + BandTall, LivingCity.UI.LedgerV2.Ink, 1f);

            _paper = new Texture2D(
                TurfPlate.RW / UploadDownsample,
                TurfPlate.RH / UploadDownsample,
                TextureFormat.RGBA32, false, false)
            {
                name = "Turf Minimap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            // Held at a fifth of its drawn size, so a point filter would drop four
            // pixels in five and shatter every kerb hairline on the sheet. This is the
            // one place on the map where the paper is read smaller than it was printed.
            _paper.filterMode = FilterMode.Bilinear;

            _sheetPose = DemoUi.NewRect("Sheet Pose", _view);
            _sheetPose.anchorMin = _sheetPose.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetPose.pivot = new Vector2(0.5f, 0.5f);
            _sheetPose.sizeDelta = Vector2.zero;

            _sheetRect = DemoUi.NewRect("Sheet", _sheetPose);
            _sheetRect.anchorMin = _sheetRect.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetRect.pivot = new Vector2(0.5f, 0.5f);
            _sheetRect.sizeDelta = new Vector2(TurfPlate.RW, TurfPlate.RH);
            var paperImage = _sheetRect.gameObject.AddComponent<RawImage>();
            paperImage.texture = _paper;
            paperImage.raycastTarget = false;

            AdoptBuildingLayer();

            // Over the paper and its volumes, under the quarter tags, the crew dots and
            // the camera's frame - the corner's copy of the full map's night wash.
            var night = DemoUi.NewRect("Night", _view);
            DemoUi.Fill(night);
            _nightInk = night.gameObject.AddComponent<Image>();
            _nightInk.raycastTarget = false;
            PaintNight();

            BuildDistrictTags();

            for (int i = 0; i < _frame.Length; i++)
            {
                _frame[i] = DemoUi.NewRect("Frame", _view);
                _frame[i].anchorMin = _frame[i].anchorMax = new Vector2(0f, 0f);
                _frame[i].pivot = new Vector2(0.5f, 0.5f);
                LivingCity.UI.LedgerKit.Fill(_frame[i], new Color32(143, 33, 25, 220));
            }

            _night.Register(_card);

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>How dark the corner plate stands right now, off the shared table so
        /// the card and the panels beside it cross together.</summary>
        void PaintNight()
        {
            if (_nightInk == null)
                return;

            var wash = HudNight.PlateWash();
            if (Mathf.Abs(_nightInk.color.a - wash.a) > 0.001f)
                _nightInk.color = wash;

            bool lit = wash.a > 0.002f;
            if (_nightInk.enabled != lit)
                _nightInk.enabled = lit;
        }

        /// <summary>
        /// The caption band: the quarter the camera is standing in, and whose street it
        /// is. Both are read off the survey's own districts - the same rectangles the
        /// plate is drawn from - so the words and the paper under them can never
        /// disagree about where the pivot is.
        /// </summary>
        void BuildBand()
        {
            _band = DemoUi.NewRect("Band", _card);
            LivingCity.UI.LedgerKit.PlaceTopLeft(_band, Border, -Border,
                CardWide, BandTall);
            LivingCity.UI.LedgerKit.Fill(_band, LivingCity.UI.LedgerV2.Head);

            var placeY = -(BandTall - LivingCity.UI.LedgerKit.LineBox(12.7f)) * 0.5f;
            _bandPlace = LivingCity.UI.LedgerKit.Caps(_band, 8f, placeY, CardWide - 130f,
                "", 12.7f, LivingCity.UI.LedgerV2.HeadCream, 18f);
            _bandPlace.font = LivingCity.UI.LedgerStyle.Condensed;
            _bandPlace.overflowMode = TMPro.TextOverflowModes.Ellipsis;

            var holderY = -(BandTall - LivingCity.UI.LedgerKit.LineBox(10.8f)) * 0.5f;
            _bandHolder = LivingCity.UI.LedgerKit.Caps(_band, CardWide - 130f, holderY, 122f,
                "", 10.8f, LivingCity.UI.LedgerV2.HeadDim, 10f,
                TMPro.TextAlignmentOptions.MidlineRight);
            _bandHolder.font = LivingCity.UI.LedgerStyle.Mono;
            _bandHolder.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        }

        /// <summary>Rewrite the caption when the camera has walked into somewhere else.
        /// Only when the words actually change: the pivot moves every frame and the
        /// quarter it is standing in does not.</summary>
        void RefreshBand()
        {
            if (_bandPlace == null)
                return;

            var place = "The city";
            var holder = "NO SURVEY";

            var districts = _survey.Districts;
            for (int i = 0; i < districts.Count; i++)
            {
                var district = districts[i];
                if (!district.World.Contains(_lastPivot))
                    continue;

                place = district.Name;
                holder = district.Contested ? "CONTESTED"
                    : district.GangId < 0 ? "NOBODY'S"
                    : district.GangId == LivingCity.Gangs.GangCatalog.PlayerGangId ? "OURS"
                    : LivingCity.Gangs.GangCatalog.Names[district.GangId].ToUpperInvariant();
                break;
            }

            if (place != _shownPlace)
            {
                _shownPlace = place;
                _bandPlace.text = place.ToUpperInvariant();
            }
            if (holder != _shownHolder)
            {
                _shownHolder = holder;
                _bandHolder.text = holder;
            }
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
            _kickTurf = _owner == null || _owner.TurfOn;
            _kickView = view;
            _fault = null;
            _state = Drawing;
            _lastKickAt = Time.unscaledTime;

            System.Threading.Tasks.Task.Run(() =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _survey.Draw(_kickView);
                    (_kickTurf ? _survey.Composite : _survey.Plain)
                        .Downsample(UploadDownsample, _uploadPixels);
                }
                catch (System.Exception fault)
                {
                    _fault = fault;
                }
                finally
                {
                    watch.Stop();
                    _lastDrawMs = watch.ElapsedMilliseconds;
                    _draws++;
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
                    _paper.SetPixelData(_uploadPixels, 0);
                    _paper.Apply(false);
                    _uploads++;
                    _printed = true;
                    _paintedTurf = _kickTurf;
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

            RefreshBand();

            // The card's chrome is built once and only ever re-lettered, so there is
            // nothing to re-register: crossing it is the whole of the night's work.
            _night.Relight();
            PaintNight();

            var pivot = _rig != null
                ? new Vector2(_rig.pivot.x, _rig.pivot.z)
                : _lastPivot;
            float heading = Heading;
            float pitch = Pitch;
            if ((pivot - _lastPivot).sqrMagnitude > 0.0025f ||
                Mathf.Abs(Mathf.DeltaAngle(_lastHeading, heading)) > 0.01f ||
                Mathf.Abs(_lastPitch - pitch) > 0.01f)
            {
                _lastPivot = pivot;
                _lastHeading = heading;
                _lastPitch = pitch;
                _lastMotionAt = Time.unscaledTime;
            }

            if (_state == Idle)
            {
                var wanted = WantedView();
                if (_survey.RefreshGeometryIfNeeded())
                {
                    AdoptBuildingLayer();
                    Kick(wanted);
                }
                else if (_painted != TurfMapHud.OwnershipStamp(_builder))
                    Kick(wanted);
                else if (_paintedTurf != (_owner == null || _owner.TurfOn))
                    Kick(wanted);
                else
                {
                    var drawn = _survey.DrawnView;
                    float pan = drawn.height > 0f
                        ? (wanted.center - drawn.center).magnitude / drawn.height
                        : float.MaxValue;
                    float zoom = drawn.height > 0f ? wanted.height / drawn.height : float.MaxValue;
                    if (zoom > 0f && zoom < 1f) zoom = 1f / zoom;
                    bool settled = Time.unscaledTime - _lastMotionAt >= RedrawAfterStillSeconds;
                    bool movingRefreshDue = Time.unscaledTime - _lastKickAt >= MovingRedrawInterval;
                    if ((pan >= RedrawPanShare || zoom >= RedrawZoomShare) &&
                        (settled || movingRefreshDue))
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

        float Heading => _rig != null ? _rig.yaw : 0f;
        float Pitch => _rig != null ? _rig.pitch : 90f;
        float Tilt => TurfMapHud.PitchTilt(Pitch);

        Vector2 ViewSize()
        {
            if (_view == null) return new Vector2(CardWide, CardTall);
            var size = _view.rect.size;
            return new Vector2(size.x > 1f ? size.x : CardWide,
                size.y > 1f ? size.y : CardTall);
        }

        float CanvasPerMetre => ViewSize().y / Mathf.Max(120f, _viewHeight);

        /// <summary>The worker draws beyond the card edges. That spare paper lets the
        /// published texture slide every frame while the next survey is in flight,
        /// instead of standing still and snapping when the replacement arrives.</summary>
        Rect WantedView()
        {
            if (_rig == null)
                return _survey.CityView;

            var card = ViewSize();
            float height = Mathf.Max(120f, _viewHeight);
            float cover = TurfMapHud.ViewCover(Heading, Tilt, card.x, card.y);
            float metresPerPixel = height * cover * ViewOverscan /
                Mathf.Max(1f, card.y);
            var span = new Vector2(TurfPlate.RW * metresPerPixel,
                TurfPlate.RH * metresPerPixel);
            var centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            return new Rect(centre - span * 0.5f, span);
        }

        void FitSheet()
        {
            var drawn = _survey.DrawnView;
            if (drawn.width <= 0f || drawn.height <= 0f)
                return;

            EnsureSheetPose();
            if (_sheetRect == null || _sheetPose == null) return;

            float canvasPerMetre = CanvasPerMetre;
            float sheetScale = drawn.height / TurfPlate.RH * canvasPerMetre;
            _sheetRect.sizeDelta = new Vector2(TurfPlate.RW, TurfPlate.RH);
            _sheetRect.localScale = Vector3.one * sheetScale;
            _sheetRect.localRotation = Quaternion.Euler(0f, 0f, Heading);
            _sheetPose.localScale = new Vector3(1f, Tilt, 1f);

            var pivot = _rig != null
                ? new Vector2(_rig.pivot.x, _rig.pivot.z)
                : drawn.center;
            _sheetRect.anchoredPosition = TurfMapHud.RotateForHeading(
                drawn.center - pivot, Heading) * canvasPerMetre;

            AdoptBuildingLayer();
        }

        /// <summary>Upgrade an already-live north-up minimap after script reload by
        /// inserting the same outer tilt pose used by TurfMap around its existing sheet.</summary>
        void EnsureSheetPose()
        {
            if (_view == null) return;

            if (_sheetPose == null)
                _sheetPose = _view.Find("Sheet Pose") as RectTransform;
            if (_sheetRect == null)
                _sheetRect = _sheetPose != null
                    ? _sheetPose.Find("Sheet") as RectTransform
                    : _view.Find("Sheet") as RectTransform;
            if (_sheetRect == null) return;

            if (_sheetPose == null)
            {
                int sibling = _sheetRect.GetSiblingIndex();
                _sheetPose = DemoUi.NewRect("Sheet Pose", _view);
                _sheetPose.SetSiblingIndex(sibling);
            }

            _sheetPose.anchorMin = _sheetPose.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetPose.pivot = new Vector2(0.5f, 0.5f);
            _sheetPose.anchoredPosition = Vector2.zero;
            _sheetPose.sizeDelta = Vector2.zero;
            _sheetPose.localRotation = Quaternion.identity;
            if (_sheetRect.parent != _sheetPose)
                _sheetRect.SetParent(_sheetPose, false);
            _sheetRect.anchorMin = _sheetRect.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetRect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Move the shared true-height city layer onto the visible postcard.</summary>
        internal void AdoptBuildingLayer()
        {
            if (_owner == null || _sheetRect == null) return;
            _buildingLayer = _owner.ShareBuildingLayer(_sheetRect, _survey,
                Heading, Pitch, 0);
            if (_buildingLayer == null) return;
            _buildingLayer.SetClipRect(CardClipRect(), true);
            if (_buildingLayer.gameObject.activeSelf != _printed)
                _buildingLayer.gameObject.SetActive(_printed);
            if (!_printed) return;
            _buildingLayer.SetView(_survey.Plan, Heading, Pitch);
        }

        Rect CardClipRect()
        {
            if (_view == null || _canvas == null) return default;
            _view.GetWorldCorners(_clipCorners);
            var root = _canvas.rootCanvas != null
                ? _canvas.rootCanvas.transform : _canvas.transform;
            var min = (Vector2)root.InverseTransformPoint(_clipCorners[0]);
            var max = (Vector2)root.InverseTransformPoint(_clipCorners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>A world point on the local card, in the camera's current view.</summary>
        bool OnCard(Vector2 worldXZ, out Vector2 at)
        {
            var size = ViewSize();
            var pivot = _rig != null
                ? new Vector2(_rig.pivot.x, _rig.pivot.z)
                : _survey.DrawnView.center;
            var local = TurfMapHud.ApplyTilt(
                TurfMapHud.RotateForHeading(worldXZ - pivot, Heading), Tilt) *
                CanvasPerMetre;
            at = size * 0.5f + local;
            return at.x >= 0f && at.y >= 0f &&
                   at.x <= size.x && at.y <= size.y;
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
            // The card draws before its own furniture exists on the frame the minimap
            // first ticks: the four rules are built with the canvas, and Update runs
            // whether or not that has happened yet.
            if (_frame[0] == null)
                return;

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

            OnCard(new Vector2(_rig.pivot.x, _rig.pivot.z), out var at);
            float halfW = across * CanvasPerMetre * 0.5f;
            float halfH = down * CanvasPerMetre * 0.5f;

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
