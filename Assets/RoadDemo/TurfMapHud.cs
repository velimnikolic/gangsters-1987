using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using LivingCity.Entities;
using LivingCity.Gameplay;
using LivingCity.CameraRig;

namespace RoadDemo
{
    /// <summary>
    /// The turf map: the whole city as a 1987 survey plate, full screen, with the
    /// outfit's crews live on top of it.
    ///
    /// ONE screen. A map over the whole viewport, one paper panel top left, one turf
    /// key pinned to the bottom right corner of the map, and a context menu at the
    /// cursor. No tabs, no second view, no chrome behind it - an earlier revision of
    /// this design had a folder, punched holes, a stat strip and three tabs around the
    /// map and every one of them was taken out on purpose.
    ///
    /// FOUR RASTER LAYERS, stacked, and only the last is redrawn per frame:
    ///
    ///   ground - terrain, water, roads, kerbs, zebras, survey grid. Drawn by
    ///            <see cref="TurfMapSurvey"/> on a worker thread.
    ///   turf   - the ownership wash, MULTIPLIED over the ground by a material, so
    ///            TURF ON/OFF is one SetActive and costs nothing.
    ///   built  - every footprint.
    ///   live   - crews, traffic, order markers, the selection box. Per frame.
    ///
    /// and one layer that is NOT raster: the street names, real type floating over the
    /// paper as children of the sheet (<see cref="TurfMapLabels"/>). They stay above
    /// the cartography but below the live tactical layer, so a crew marker or its route
    /// never disappears under a name. The design forbids baking them - a name printed
    /// into the paper magnifies with the paper.
    ///
    /// THE SURVEY RUNS OFF THE MAIN THREAD. A plate is thirty milliseconds and the
    /// wheel asks for one several times a second; drawn in Update that is two dropped
    /// frames every time, which is the stutter this map had. So a draw is handed to the
    /// thread pool and the finished plate is uploaded on the frame it comes back. In
    /// between - and that is most frames while the boom is moving - the sheet already on
    /// screen is simply scaled and slid to stand in, which is what makes the wheel feel
    /// continuous while the paper keeps up at its own pace.
    ///
    /// The plate is 960 x 600 and is fitted over the viewport the way an image with
    /// object-fit: cover is, then turned to the street camera's heading. It always fills
    /// the screen and the overflow is cropped. Every pointer position has to undo that
    /// crop and turn before it means anything - see <see cref="ToPlan"/>. Getting it
    /// wrong silently offsets every click on the map, which is the single easiest bug
    /// to ship here.
    ///
    /// Everything the player ORDERS is remembered in world metres, never in the plate's
    /// authored units. A survey publishes a new projection whenever the boom moves, and
    /// a patrol box or an order marker held in units of the old one slides off the
    /// ground it was put on.
    ///
    /// It comes up the way the old plan did and on the same line: pull the wheel back
    /// past the configured <see cref="DemoCamera.mapAt"/> threshold and the city stops being a place and
    /// becomes a PLAN; push in past it and the streets come back exactly where they
    /// were. There is no key for it and there must not be one - the map is a zoom
    /// level, not a screen, and a key that opened it would be a second truth about
    /// where the player is looking.
    ///
    /// Because it IS the boom, the camera rig stays live the whole time: the wheel is
    /// the only way back down, the street camera's yaw carries straight into the plan,
    /// WASD pans by that same heading, and the boom between the map line and the ceiling
    /// drives the plate's own scale - cover at the line, the whole sheet in frame at the
    /// top of the wheel.
    ///
    /// Sizes and line weights all come off <see cref="TurfPlate.S"/> rather than being
    /// written per element, so the same code draws the corner minimap.
    /// </summary>
    public sealed class TurfMapHud : MonoBehaviour
    {
        /// <summary>Over the demo's own bars (20, 22) and the world overlays, under
        /// the personnel ledger (110) - the book must stay readable if P is pressed
        /// over the map, the almanac's own convention.</summary>
        const int SortingOrder = 60;

        /// <summary>Authored units a click may miss a crew by and still take it. Two
        /// and a half is a man's own dot plus the shake of a mouse.</summary>
        const float PickRadius = 2.5f;

        /// <summary>Authored units of drag before a click becomes a marquee.</summary>
        const float DragSlop = 2f;

        /// <summary>The patrol box WALKING re-homes a crew to, in METRES - a city block
        /// and a bit. Not authored units: a box measured on the plate is a different
        /// piece of ground at every zoom, so a crew told to walk a block would be given
        /// a different block depending on where the wheel happened to be.</summary>
        const float ZoneWide = 80f, ZoneDeep = 60f;

        /// <summary>How long an order marker lives. The design's eighty frames at sixty
        /// a second - but counted in SECONDS, so it is the same second and a third on
        /// every machine.</summary>
        const float MarkerSeconds = 1.33f;

        /// <summary>Metres from a target before a crew counts as arrived. A crew is
        /// several men wide and they arrive one at a time.</summary>
        const float ArriveMetres = 14f;

        /// <summary>Prints what every survey cost. Off by default: the map redraws
        /// several times a second while the boom is moving and a line each would be the
        /// loudest thing in the console.</summary>
        public bool logSurveys;

        /// <summary>Whether the same plate is also printed small into the corner of the
        /// screen while the player is down in the street.</summary>
        public bool minimap = true;

        /// <summary>True while the map is up - the keyboard half of the modal shield,
        /// the same convention as PersonnelAlmanac.IsOpen.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while open AND on the frame it closes: Esc readers poll,
        /// polling cannot consume, and Update order is arbitrary - a reader running
        /// after the close would otherwise act on the very press that closed it.
        /// </summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == _lastCloseFrame;

        static int _lastCloseFrame = -1;

        /// <summary>A turf map exists in this scene and is the one map on the wheel's
        /// line. Cleared when the map is destroyed with the scene, like IsOpen: a
        /// static left standing outlives the scene it described.</summary>
        public static bool Installed { get; private set; }

        /// <summary>True while the map is up and the pointer is on its paper - the
        /// panel, the key or the context menu - rather than on the plate. The camera
        /// reads it so the wheel over the roster scrolls the roster and does not also
        /// move the boom.</summary>
        public static bool PointerOverChrome { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            IsOpen = false;
            Installed = false;
            PointerOverChrome = false;
            _lastCloseFrame = -1;
        }

        /// <summary>
        /// Whether a crew may take a building by standing on it, and the seam a real
        /// campaign rule replaces. Ground in this project is taken premise by premise
        /// and the rule for HOW is deliberately unwritten (Outfit.Turf's note); the map
        /// must not invent one. The default below is the smallest honest stub - the
        /// crew has to actually be there and nobody else's men may be standing on it -
        /// and the ownership it writes is BusinessMarker.GangId, the project's single
        /// source for who holds ground, so the ledger sees a takeover the same frame.
        /// </summary>
        public static System.Func<TurfBuilding, TurfCrew, bool> ClaimRule;

        // ------------------------------------------------------------------ wiring

        RoadDemoBuilder _builder;
        Transform _blockRoot;
        DemoCrews _crews;
        CrewOverlay _crewOverlay;
        BuildingCardPicker _picker;
        DemoCamera _rig;
        List<DemoVehicle> _traffic;
        List<PolicePatrolCar> _policeCars;

        readonly TurfMapSurvey _survey = new TurfMapSurvey();
        readonly TurfPlate _live = new TurfPlate();
        TurfMapLabels _lettering;
        TurfMapBuildingLayer _buildingLayer;

        Texture2D _groundTex, _liveTex;
        RawImage _groundImage, _liveImage;
        RectTransform _sheetPose, _sheet;
        Canvas _canvas;
        TurfMapPanel _mapChrome;
        TurfMapPanel _crewPanel;

        readonly List<TurfCrew> _units = new List<TurfCrew>();
        readonly List<Marker> _markers = new List<Marker>();
        readonly List<Vector3> _movementPath = new List<Vector3>();
        List<CrewEnemyAction> _enemyActions = new List<CrewEnemyAction>();

        /// <summary>Selected crews, by DemoCrews unit id. Only ever ours.</summary>
        readonly List<int> _selected = new List<int>();

        TurfCrew _inspectedCrew;
        TurfBuilding _inspectedBuilding;
        TurfDistrict _inspectedDistrict;
        bool _crewFileRequested;
        int _seenPersonnelVersion = -1;

        bool _dragging, _dragMoved;
        float _lastRightOrderAt = -10f;
        Vector2 _lastRightOrderScreen;

        const float DoubleRightClick = 0.55f;
        const float DoubleRightSlack = 60f;

        /// <summary>The ground chosen by the wheel event that crossed back into the
        /// street. Banked for the following Update, when Show(false) takes the map down.</summary>
        Vector2? _landingTarget;

        /// <summary>The marquee's two corners, in WORLD METRES. A survey landing
        /// mid-drag republishes the projection; a box held in authored units would jump
        /// with it.</summary>
        Vector2 _dragFrom, _dragTo;

        int _paintedOwnership = -1;

        /// <summary>Where an order landed, in world metres, and how long it has left.
        /// </summary>
        struct Marker
        {
            public Vector2 World;
            public float Life;
            public TurfOrder Order;
        }

        public bool TurfOn { get; private set; } = true;

        public TurfMapSurvey Survey => _survey;
        public TurfMapBuildingLayer BuildingLayer => _buildingLayer;
        public IReadOnlyList<TurfCrew> Units => _units;
        public IReadOnlyList<int> Selected => _selected;

        /// <summary>Whether this crew is one of the gathered. A list lookup rather
        /// than a set: a selection is a handful of crews and the panel asks once a
        /// row.</summary>
        public bool IsGathered(int crewId) => _selected.Contains(crewId);
        public TurfCrew InspectedCrew => _crewFileRequested ? _inspectedCrew : null;
        public TurfBuilding InspectedBuilding => _inspectedBuilding;
        public TurfDistrict InspectedDistrict => _inspectedDistrict;

        public void Init(RoadDemoBuilder city, Transform blocks, BuildingCardPicker cardPicker,
            DemoCamera camera, DemoCrews streetCrews,
            List<DemoVehicle> cars, List<PolicePatrolCar> patrols)
        {
            _builder = city;
            _blockRoot = blocks;
            _picker = cardPicker;
            _rig = camera;
            _crews = streetCrews;
            _crewOverlay = streetCrews != null ? streetCrews.GetComponent<CrewOverlay>() : null;
            _traffic = cars;
            _policeCars = patrols;
        }

        // -------------------------------------------------------------------- life

        void Start()
        {
            _survey.Prepare(_builder, _blockRoot);
            if (!_survey.Ready)
            {
                enabled = false;
                return;
            }

            BuildCanvas();
            _inspectedCrew = null;
            _crewFileRequested = false;
            _inspectedBuilding = null;
            _inspectedDistrict = null;
            BuildCrewPanel();

            // The ruler has to exist before the first draw: the crossings pass steers
            // around the street names, and it runs where the face cannot be asked
            // anything.
            _survey.MeasureNames(_lettering.Measure);
            CollectCrews();
            _crewPanel.SelectionChanged();
            _crewPanel.Refresh();

            // One draw of the whole city, here and not on a worker: the first frame the
            // wheel opens has to have paper on it, and thirty milliseconds inside a
            // scene build nobody is looking at is free.
            DrawNow(_survey.CityView);

            // How far back the wheel may go: the TOWN in the frame with a hand's width
            // of country round it, not the whole island. The screen shows
            // distance * BoomToMetres metres down its height, and the survey's own
            // city view is already the grid with a margin, fitted to the plate's
            // aspect - so the ceiling is the boom at which that view fills the height.
            if (_rig != null)
            {
                var city = _survey.CityView;
                float wants = Mathf.Max(city.height, city.width / TurfPlate.AW * TurfPlate.AH);
                _rig.mapCeiling = Mathf.Clamp(
                    wants * CityFrame / DemoCamera.BoomToMetres, 260f, 440f);
            }

            // And the same plate in the corner, for the player down in the street. It
            // is installed from HERE and not by the city builder so it can borrow this
            // survey's coastline: sampling the island twice is a pause at load, and
            // both maps are drawing the same island.
            if (minimap)
            {
                var corner = new GameObject("Turf Minimap");
                corner.transform.SetParent(transform, false);
                corner.AddComponent<TurfMinimap>()
                    .Init(_builder, _blockRoot, _rig, _crews, _survey);
            }

            Installed = true;

            // Down until the wheel goes past the map line. Not Show(false): the map has
            // never been open, so Show would see no change and leave the canvas
            // standing over the street.
            _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Blank(false);
            foreach (var texture in new[] { _groundTex, _liveTex })
                if (texture != null)
                    Destroy(texture);

            // The statics describe THIS map. A scene unloaded with the map up would
            // otherwise leave the camera's hint off and the top bar retracted for the
            // rest of the session, waiting on a map that no longer exists.
            Installed = false;
        }

        void OnDisable()
        {
            // Never leave the world switched off behind a plan that is no longer there,
            // nor the street's picker, nor the "map is up" flag every other screen reads.
            Blank(false);
            if (_picker)
                _picker.enabled = true;
            if (IsOpen)
            {
                IsOpen = false;
                _lastCloseFrame = Time.frameCount;
            }
            PointerOverChrome = false;
        }

        // ------------------------------------------------------- the world behind

        Camera _world;
        int _worldMask;
        CameraClearFlags _worldClear;
        Color _worldBackground;
        bool _worldPost;

        /// <summary>
        /// While the paper covers the screen the city behind it is drawn for nobody.
        /// The old plan's own trick, and the reason it was smooth: the camera is told
        /// to render NOTHING, so the frame costs a clear instead of eight million
        /// verts, a thousand lamps and the whole crowd - all of it in frustum at once,
        /// because the map is the furthest the boom ever goes.
        ///
        /// The restore is guarded on having saved a mask of its own, so a second
        /// caller that blanked the camera first is never handed a zero mask back.
        /// </summary>
        void Blank(bool on)
        {
            if (_world == null && _rig != null)
                _world = _rig.GetComponent<Camera>();
            if (_world == null)
                return;

            if (on)
            {
                if (_world.cullingMask == 0)
                    return;
                _worldMask = _world.cullingMask;
                _worldClear = _world.clearFlags;
                _worldBackground = _world.backgroundColor;
                _world.cullingMask = 0;
                _world.clearFlags = CameraClearFlags.SolidColor;
                _world.backgroundColor = new Color32(230, 218, 185, 255);
                var data = _world.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    _worldPost = data.renderPostProcessing;
                    data.renderPostProcessing = false;
                }
            }
            else if (_world.cullingMask == 0 && _worldMask != 0)
            {
                _world.cullingMask = _worldMask;
                _world.clearFlags = _worldClear;
                _world.backgroundColor = _worldBackground;
                var data = _world.GetUniversalAdditionalCameraData();
                if (data != null)
                    data.renderPostProcessing = _worldPost;
            }
        }

        void Update()
        {
            // The plan is up for exactly as long as the boom is past the line - and
            // never under the book, which owns the whole screen when it is open.
            bool want = _rig != null && _rig.MapOut &&
                        !LivingCity.UI.PersonnelAlmanac.IsOpen;
            if (want != IsOpen)
                Show(want);

            // The roster is present in the 3D city as well as on the plan. Only the
            // Personal File section is conditional on an explicit lieutenant click.
            if (_crewPanel != null && !_crewPanel.gameObject.activeSelf)
                _crewPanel.gameObject.SetActive(true);
            RefreshCrewDossiers();
            _crewPanel.Refresh();

            if (!IsOpen)
                return;

            // Esc is the wheel's shortcut: it does not hide the plan, it puts the
            // camera back down in the street, which is the only thing that can close a
            // map that IS a zoom level.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_mapChrome != null && _mapChrome.MenuOpen)
                    _mapChrome.CloseMenu();
                else
                    Descend();
                return;
            }

            PumpSurvey();
            FitSheet();
            Pointer();
            Zoom();
            Steer();
            DrawLive();

            _mapChrome.Refresh();
        }

        void Show(bool on)
        {
            if (IsOpen == on)
                return;

            IsOpen = on;
            if (!on)
                _lastCloseFrame = Time.frameCount;

            _canvas.gameObject.SetActive(on);
            Blank(on);

            // The street below is not being looked at, so its own building card must
            // not answer a click that landed on the plate. The camera RIG stays live:
            // the wheel is the only way back down, and disabling it would strand the
            // player on the map.
            if (_picker)
                _picker.enabled = !on;

            if (on)
            {
                _landingTarget = null;
                CollectCrews();
                _crewPanel.SelectionChanged();

                // The live layer still carries the last frame it was drawn on, in a
                // projection that is about to be thrown away - crews and cars would
                // flash once in the wrong places before the first DrawLive of the new
                // view lands on top of them.
                _live.Clear(new Color32(0, 0, 0, 0));
                _live.Apply(_liveTex);

                // The paper already on the sheet is the whole city, and FitSheet knows
                // how to scale it onto this view. So the map opens on something correct
                // straight away and the survey for THIS view arrives a frame or two
                // later - which is better than opening on a thirty millisecond hitch.
                FitSheet();
                PumpSurvey();
            }
            else
            {
                // Going down into the street: the player lands on the ground he had the
                // pointer over, not on whatever happened to be under the middle of the
                // plan. The old plan's rule, and the reason coming back up feels like
                // the same camera rather than a second one.
                Land();
                _dragging = false;
                _mapChrome.CloseMenu();
                PointerOverChrome = false;
            }
        }

        /// <summary>Puts the boom back on the street side of the map line - what Esc
        /// means on a map that is a zoom level.</summary>
        void Descend()
        {
            if (_rig != null)
                _rig.distance = Mathf.Max(_rig.minDistance, _rig.mapAt - 15f);
        }

        void Land()
        {
            if (_rig == null)
                return;

            Vector2 ground;
            if (_landingTarget.HasValue)
            {
                ground = _landingTarget.Value;
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null || !TryGroundAt(mouse.position.ReadValue(), out ground))
                    return;
            }

            _rig.pivot = new Vector3(ground.x, _rig.pivot.y, ground.y);
            _rig.Drop();
            _landingTarget = null;
        }

        /// <summary>
        /// The turf map owns the wheel while it is visible. Every intermediate step
        /// keeps the same ground under the pointer; the step that crosses mapAt banks
        /// that ground as the street camera's new centre. Letting DemoCamera also read
        /// the wheel here would apply the distance twice in one frame.
        /// </summary>
        void Zoom()
        {
            if (_rig != null && _rig.SuppressInput)
                return;
            var mouse = Mouse.current;
            if (_rig == null || mouse == null || PointerOverChrome)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            float previous = _rig.distance;
            float next = _rig.DistanceAfterWheel(scroll);
            if (Mathf.Approximately(previous, next))
                return;

            if (!TryGroundAt(mouse.position.ReadValue(), out var anchor))
            {
                _rig.distance = next;
                return;
            }

            var pivot = new Vector2(_rig.pivot.x, _rig.pivot.z);
            bool enteringStreet = previous > _rig.mapAt && next <= _rig.mapAt;
            if (enteringStreet)
            {
                // The old sheet remains on screen for this final frame. The next frame
                // opens the street already centred on exactly what the cursor named.
                pivot = anchor;
                _landingTarget = anchor;
            }
            else
            {
                pivot = PinnedZoomPivot(pivot, anchor, previous, next);
                _landingTarget = null;
            }

            _rig.pivot = new Vector3(pivot.x, _rig.pivot.y, pivot.y);
            _rig.distance = next;
            _rig.Drop();
        }

        bool TryGroundAt(Vector2 screen, out Vector2 ground)
        {
            var plan = ToPlan(screen);
            if (plan.x < 0f || plan.y < 0f ||
                plan.x > TurfPlate.AW || plan.y > TurfPlate.AH)
            {
                ground = default;
                return false;
            }

            ground = _survey.Plan.ToWorld(plan);
            return true;
        }

        // ------------------------------------------------------------------ canvas

        void BuildCanvas()
        {
            var go = new GameObject("Turf Map Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

            // The design's numbers - 11 px lettering, an 8 px floor in the panel, a
            // 24 px close box - are read at 1080 lines, which is what the sheet they
            // were drawn on measured. On a taller window they are that fraction of the
            // window and not that count of pixels, exactly as every other screen in
            // this game is built; a constant-pixel canvas leaves the panel and the
            // clock at a sixth of their intended size on a 4K display.
            //
            // The PLATE is not on this ladder. It is scaled off the boom in FitSheet,
            // in real screen pixels, and divided back out by this factor - so paper
            // stays 1:1 with the screen while the furniture on it grows.
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 720 lines, not 1080: the design's own numbers are small on purpose - an
            // 8 px floor in the panel, 9 px in the key - and read against a 1080 sheet
            // they come out under six real pixels on any window shorter than that.
            // Against 720 the smallest type in the plan is never below its drawn size,
            // and on a tall window everything grows together.
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();

            // The paper the sheet is laid on. Only seen once the wheel has pulled far
            // enough back that the plate no longer covers the window - but a bare
            // canvas there would show the street through it.
            var backdrop = DemoUi.NewRect("Backdrop", go.transform);
            DemoUi.Fill(backdrop);
            LivingCity.UI.LedgerKit.Fill(backdrop, new Color32(230, 218, 185, 255));

            // The sheet is exactly the plate, and it is SCALED rather than resized.
            // That is what lets the street names be children of it: a name positioned
            // in plate pixels and set at a size in plate pixels then rides the boom
            // with the paper, and neither has to be told the wheel moved.
            // Pose is separate from the sheet because pitch squashes SCREEN vertical
            // after heading has turned the ground. A single RectTransform would scale
            // the map's north axis before rotating it and tilt around the wrong line.
            _sheetPose = DemoUi.NewRect("Sheet Pose", go.transform);
            _sheetPose.anchorMin = _sheetPose.anchorMax = new Vector2(0.5f, 0.5f);
            _sheetPose.pivot = new Vector2(0.5f, 0.5f);
            _sheetPose.sizeDelta = Vector2.zero;

            _sheet = DemoUi.NewRect("Sheet", _sheetPose);
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0.5f);
            _sheet.pivot = new Vector2(0.5f, 0.5f);
            _sheet.sizeDelta = new Vector2(TurfPlate.RW, TurfPlate.RH);

            _groundTex = TurfPlate.NewTexture("Turf Static Sheet");
            _liveTex = TurfPlate.NewTexture("Turf Live");

            _groundImage = Layer("Ground", _groundTex, null);

            // True-height building massing lives after the flat survey footprints and
            // before every live marker/name. It is model-derived and persistent, not a
            // second look at whichever streamed holders happen to be resident.
            EnsureBuildingLayer();
            _liveImage = Layer("Live", _liveTex, null);

            // Over the cartography, under the live tactical layer and panel. Street
            // names remain vector-sharp, while crews and their I-key routes retain
            // priority when the two occupy the same stretch of road.
            //
            // No mask on the sheet, deliberately. A RectMask2D here would be the tidy
            // way to cut a name at the paper's edge, but it re-materialises every
            // graphic under it to carry a clip rectangle - including the turf wash,
            // whose whole job is a multiply blend. The names are placed along the run
            // of street that is actually on the sheet, so the most any of them
            // overhangs is half a word.
            _lettering = go.AddComponent<TurfMapLabels>();
            _lettering.Attach(_sheet);
            EnsureMapLayerOrder();

            _mapChrome = go.AddComponent<TurfMapPanel>();
            _mapChrome.Init(this, showPanel: false, showMapChrome: true);
        }

        void BuildCrewPanel()
        {
            var go = new GameObject("Crew File Canvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 115;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();

            _crewPanel = go.AddComponent<TurfMapPanel>();
            _crewPanel.Init(this, showPanel: true, showMapChrome: false);
        }

        RawImage Layer(string name, Texture2D texture, Material material)
        {
            var rect = DemoUi.NewRect(name, _sheet);
            DemoUi.Fill(rect);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            if (material != null)
                image.material = material;
            return image;
        }

        // ------------------------------------------------------------ the view

        /// <summary>How much more of the town than the town the ceiling shows: the
        /// city in the frame with a hand's width of country round it.</summary>
        const float CityFrame = 1.25f;

        /// <summary>
        /// How much more ground the plate carries than the window can show. Cover
        /// alone fits the sheet to the window EXACTLY: one axis ends up with no
        /// margin at all, so the first step of a pan slides bare paper in from the
        /// edge before any survey can answer. A quarter over means a tenth of the
        /// plate hangs off each side - room for the sheet to slide into while the next
        /// survey is being drawn. It is paid for in resolution: a pixel is a quarter
        /// more metres than it would otherwise be.
        /// </summary>
        const float Overscan = 1.25f;

        /// <summary>Drift under which a redraw is not worth its milliseconds - the
        /// texture transform below covers it with no visible loss. There is no second,
        /// larger threshold and no settle timer any more: a draw costs the frame
        /// nothing, so the only question worth asking is whether the paper on screen
        /// has drifted far enough to be worth replacing.</summary>
        const float EasyZoom = 1.06f, EasyPan = 0.035f;

        float _sheetScale = 1f, _sheetHeading, _sheetTilt = 1f;
        float _indicatorScale = 1f;
        Vector2 _sheetAt;

        float Heading => _rig != null ? _rig.yaw : 0f;
        float Tilt => _rig != null ? PitchTilt(_rig.pitch) : 1f;

        /// <summary>
        /// What rectangle of ground the plate ought to be drawn for right now.
        ///
        /// The screen shows <c>distance * DemoCamera.BoomToMetres</c> metres down its
        /// height - the camera's own reading of the boom. The plate is displayed cover-fit,
        /// so only part of it is on screen; scaling that back out gives the ground the
        /// WHOLE 960 x 600 sheet has to carry, centred on the camera's own pivot.
        /// </summary>
        Rect WantedView()
        {
            if (_rig == null)
                return _survey.CityView;

            float down = Mathf.Max(40f, _rig.distance * DemoCamera.BoomToMetres);
            float cover = ViewCover(Heading, Tilt, Screen.width, Screen.height);
            float metresPerPixel = down * cover * Overscan / Mathf.Max(1f, Screen.height);
            var span = new Vector2(TurfPlate.RW * metresPerPixel, TurfPlate.RH * metresPerPixel);
            var centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            return new Rect(centre - span * 0.5f, span);
        }

        // ------------------------------------------------------------- the survey

        const int Idle = 0, Drawing = 1, Drawn = 2;

        /// <summary>Which end of the draw owns the plates. Written by the main thread
        /// on the way into a draw and by the worker on the way out, and read on both
        /// sides - so it is volatile, and every transition has exactly one writer.
        /// </summary>
        volatile int _state = Idle;

        Rect _kickView;
        System.Exception _kickFault;
        long _kickMs;
        int _surveys;
        float _lastPublishMs, _worstPublishMs;
        int _staticUploads;

        public float LastPublishMs => _lastPublishMs;
        public float WorstPublishMs => _worstPublishMs;
        public int StaticUploads => _staticUploads;

        /// <summary>Who held what when the draw in flight was handed over. Stamped onto
        /// the plate when it lands, NOT the reading taken at that moment: a takeover
        /// that happens while the survey is drawing is not on the plate the survey
        /// returns, and crediting the plate with it would mean never drawing it.
        /// </summary>
        int _kickOwnership = -1;

        /// <summary>Ground changed hands, so the wash and the footprints are wrong
        /// whatever the boom is doing. Forces the next draw regardless of drift.
        /// </summary>
        bool _ownershipStale;

        /// <summary>One survey, here and now. Only at build, when a hitch nobody is
        /// looking at buys a first plate to open on.</summary>
        void DrawNow(Rect view)
        {
            SnapshotOwners();
            _survey.Draw(view);
            Publish();
        }

        /// <summary>What the drawing passes need that only a MonoBehaviour can answer,
        /// read here on the main thread and banked - the footprints' owners, and the
        /// fingerprint of them that decides when the plate is next wrong.</summary>
        void SnapshotOwners()
        {
            _survey.ReadOwners();
            _kickOwnership = OwnershipStamp(_builder);
            _ownershipStale = false;
        }

        /// <summary>
        /// Hands one draw to the thread pool. The owner snapshot is taken HERE, on the
        /// main thread, because it is the one thing the drawing passes need that only
        /// a MonoBehaviour can answer.
        /// </summary>
        void Kick(Rect view)
        {
            if (_state != Idle)
                return;

            SnapshotOwners();
            _kickView = view;
            _kickFault = null;
            _state = Drawing;

            System.Threading.Tasks.Task.Run(() =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _survey.Draw(_kickView);
                }
                catch (System.Exception fault)
                {
                    // A worker's exception is swallowed by the task and the map would
                    // simply stop redrawing with no word anywhere. Carried back and
                    // logged where a log means something.
                    _kickFault = fault;
                }
                finally
                {
                    _kickMs = watch.ElapsedMilliseconds;
                    _state = Drawn;
                }
            });
        }

        /// <summary>
        /// Takes delivery of a finished plate and decides whether to ask for the next
        /// one. Between the two the sheet on screen is scaled and slid to stand in,
        /// which is why the map still moves at sixty frames a second while the survey
        /// runs at its own pace.
        /// </summary>
        void PumpSurvey()
        {
            if (_state == Drawn)
            {
                if (_kickFault != null)
                    Debug.LogError("[TurfMap] survey failed: " + _kickFault);
                else
                    Publish();

                _state = Idle;
            }

            if (_state != Idle)
                return;

            var want = TurfMapSurvey.FitToPlate(WantedView());
            if (_survey.RefreshGeometryIfNeeded())
            {
                EnsureBuildingLayer();
                if (_inspectedBuilding != null &&
                    !_survey.Buildings.Contains(_inspectedBuilding))
                    _inspectedBuilding = null;
                Kick(want);
                return;
            }

            var drawn = _survey.DrawnView;
            if (drawn.height <= 0f)
            {
                Kick(want);
                return;
            }

            if (_ownershipStale || _paintedOwnership != OwnershipStamp(_builder))
            {
                Kick(want);
                return;
            }

            float zoom = want.height / drawn.height;
            if (zoom < 1f)
                zoom = 1f / zoom;
            float pan = (want.center - drawn.center).magnitude / drawn.height;

            if (zoom >= EasyZoom || pan >= EasyPan)
                Kick(want);
        }

        /// <summary>The finished plate onto the screen: the pixels, the projection
        /// every hit test runs through, and the lettering that belongs to it.</summary>
        void Publish()
        {
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            _survey.Publish();
            PushStatic();
            _lettering.Set(_survey.Labels);
            _paintedOwnership = _kickOwnership;

            _lastPublishMs = (System.Diagnostics.Stopwatch.GetTimestamp() - began) *
                1000f / System.Diagnostics.Stopwatch.Frequency;
            _worstPublishMs = Mathf.Max(_worstPublishMs, _lastPublishMs);

            _surveys++;
            if (logSurveys)
                Debug.Log($"[TurfMap] survey {_surveys}: {_kickMs} ms over " +
                          $"{_survey.DrawnView.width:0} x {_survey.DrawnView.height:0} m");
        }

        /// <summary>
        /// Where the drawn sheet has to sit so the ground it carries lines up with the
        /// ground the camera is looking at NOW. On a settled view this is exactly
        /// cover-fit and dead centre; between a move and its redraw it is the same
        /// paper slid and scaled, which is what makes the wheel feel continuous.
        /// </summary>
        void FitSheet()
        {
            var drawn = _survey.DrawnView;
            if (drawn.height <= 0f || _sheet == null)
                return;

            EnsureSheetPose();
            if (_sheetPose == null)
                return;

            float down = _rig != null
                ? Mathf.Max(40f, _rig.distance * DemoCamera.BoomToMetres)
                : drawn.height;
            float screenPerMetre = Mathf.Max(1f, Screen.height) / down;

            _sheetScale = drawn.height / TurfPlate.RH * screenPerMetre;
            float ui = UiScale;

            // Keep lettering naturally readable when close, but let it recede with the
            // map at the wide end instead of leaving every street name at one screen
            // size while the city becomes smaller underneath it.
            if (_lettering != null && _rig != null)
            {
                float zoomOut = Mathf.InverseLerp(_rig.mapAt, _rig.mapCeiling,
                    _rig.distance);
                _lettering.SetZoomOut(zoomOut);
                _indicatorScale = Mathf.Lerp(1f, 0.18f, zoomOut);
            }

            // SCALED, not resized: the sheet keeps its 960 x 600 so everything hung on
            // it - every street name - keeps its own place and its own point size in
            // plate pixels, and rides the boom for free.
            _sheet.localScale = Vector3.one * (_sheetScale / ui);

            // The turf plan is the same camera flattened onto paper: yaw turns it and
            // pitch foreshortens the camera-forward ground axis. The outer pose applies
            // that screen-vertical squash after the inner sheet has turned.
            _sheetHeading = Heading;
            _sheetTilt = Tilt;
            _sheetPose.localScale = new Vector3(1f, _sheetTilt, 1f);
            _sheet.localRotation = Quaternion.Euler(0f, 0f, _sheetHeading);

            var pivot = _rig != null
                ? new Vector2(_rig.pivot.x, _rig.pivot.z)
                : drawn.center;
            var uncompressedAt = RotateForHeading(
                drawn.center - pivot, _sheetHeading) * screenPerMetre;
            _sheetAt = ApplyTilt(uncompressedAt, _sheetTilt);
            _sheet.anchoredPosition = uncompressedAt / ui;

            EnsureBuildingLayer();
            if (_buildingLayer != null)
                _buildingLayer.SetView(_survey.Plan, _sheetHeading,
                    _rig != null ? _rig.pitch : 90f);
        }

        /// <summary>Adopts/installs the volume layer after a script reload in Play.
        /// Existing sheet layers may predate the field; the final order is static map,
        /// volumes, street names, then live tactical marks.</summary>
        void EnsureBuildingLayer()
        {
            if (_buildingLayer == null && _sheet != null)
            {
                var root = _sheet.Find("Building Volumes") as RectTransform;
                if (root == null)
                {
                    root = DemoUi.NewRect("Building Volumes", _sheet);
                    DemoUi.Fill(root);
                }
                _buildingLayer = root.GetComponent<TurfMapBuildingLayer>();
                if (_buildingLayer == null)
                    _buildingLayer = root.gameObject.AddComponent<TurfMapBuildingLayer>();
            }

            if (_buildingLayer != null && _builder != null &&
                _buildingLayer.GeometryVersion != _builder.ResidentialGeometryVersion)
            {
                _buildingLayer.PreparePose(Heading, _rig != null ? _rig.pitch : 90f);
                _buildingLayer.Rebuild(_builder, _survey);
            }

            EnsureMapLayerOrder();
        }

        /// <summary>Live people and order graphics must win over street lettering.
        /// Reasserted from the hot-reload adoption path as well as on a fresh build,
        /// because an already-open map can have been assembled by the previous script.</summary>
        void EnsureMapLayerOrder()
        {
            if (_sheet == null)
                return;

            if (_buildingLayer != null && _groundImage != null)
            {
                var volumes = _buildingLayer.transform;
                int afterGround = _groundImage.transform.GetSiblingIndex() + 1;
                if (volumes.GetSiblingIndex() != afterGround)
                    volumes.SetSiblingIndex(afterGround);
            }

            if (_liveImage != null &&
                _liveImage.transform.GetSiblingIndex() != _sheet.childCount - 1)
                _liveImage.transform.SetAsLastSibling();
        }

        /// <summary>Adopts a sheet built before this field existed when scripts reload
        /// during Play. A fresh map already has the pose from BuildCanvas.</summary>
        void EnsureSheetPose()
        {
            if (_sheetPose != null || _sheet == null)
                return;

            var oldParent = _sheet.parent;
            if (oldParent == null)
                return;
            int sheetSibling = _sheet.GetSiblingIndex();

            _sheetPose = oldParent.name == "Sheet Pose"
                ? oldParent as RectTransform
                : oldParent.Find("Sheet Pose") as RectTransform;
            if (_sheetPose == null)
            {
                _sheetPose = DemoUi.NewRect("Sheet Pose", oldParent);
                _sheetPose.anchorMin = _sheetPose.anchorMax = new Vector2(0.5f, 0.5f);
                _sheetPose.pivot = new Vector2(0.5f, 0.5f);
                _sheetPose.sizeDelta = Vector2.zero;
                _sheetPose.SetSiblingIndex(sheetSibling);
            }

            if (_sheet.parent != _sheetPose)
                _sheet.SetParent(_sheetPose, false);
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0.5f);
            _sheet.pivot = new Vector2(0.5f, 0.5f);
            _sheet.sizeDelta = new Vector2(TurfPlate.RW, TurfPlate.RH);
        }

        /// <summary>Canvas units per screen pixel. Anything positioned from a screen
        /// reading - the sheet itself, a chip dropped on a district - has to come back
        /// through this or it lands at a fraction of the distance on a big window.
        /// </summary>
        public float UiScale => _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;

        /// <summary>The scale an axis-aligned survey plate needs to cover a window after
        /// it is turned by the camera heading. Without the sine terms a diagonal view
        /// exposes the paper backdrop through two corners while a new survey is drawn.</summary>
        internal static float HeadingCover(float degrees, float screenWidth, float screenHeight)
            => ViewCover(degrees, 1f, screenWidth, screenHeight);

        /// <summary>The cover scale after the camera pitch has foreshortened screen Y.
        /// Inverse-transforming the window first gives the exact local rectangle the
        /// rotated survey sheet must carry.</summary>
        internal static float ViewCover(float degrees, float tilt,
            float screenWidth, float screenHeight)
        {
            tilt = Mathf.Max(0.01f, tilt);
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Abs(Mathf.Cos(radians));
            float sine = Mathf.Abs(Mathf.Sin(radians));
            float untiltedHeight = screenHeight / tilt;
            return Mathf.Max(
                (cosine * screenWidth + sine * untiltedHeight) / TurfPlate.RW,
                (sine * screenWidth + cosine * untiltedHeight) / TurfPlate.RH);
        }

        /// <summary>Orthographic ground-plane foreshortening for the street camera's
        /// pitch: horizontal at zero, top-down at ninety degrees.</summary>
        internal static float PitchTilt(float pitch) => Mathf.Max(0.01f,
            Mathf.Sin(Mathf.Clamp(pitch, 0f, 90f) * Mathf.Deg2Rad));

        /// <summary>World east/north into screen right/up at the camera's heading.</summary>
        internal static Vector2 RotateForHeading(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                cosine * value.x - sine * value.y,
                sine * value.x + cosine * value.y);
        }

        internal static Vector2 ApplyTilt(Vector2 value, float tilt) =>
            new Vector2(value.x, value.y * Mathf.Max(0.01f, tilt));

        internal static Vector2 RemoveTilt(Vector2 value, float tilt) =>
            new Vector2(value.x, value.y / Mathf.Max(0.01f, tilt));

        /// <summary>Moves the camera pivot just enough for an anchor to retain the same
        /// screen position when the boom changes length.</summary>
        internal static Vector2 PinnedZoomPivot(Vector2 pivot, Vector2 anchor,
            float previousDistance, float nextDistance)
        {
            if (previousDistance <= 0.0001f)
                return pivot;
            return pivot + (anchor - pivot) * (1f - nextDistance / previousDistance);
        }

        /// <summary>The rotated sheet's pivot in screen pixels.</summary>
        Vector2 SheetCenter => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + _sheetAt;

        /// <summary>Screen point to authored units on the sheet AS DRAWN. Undoing the
        /// camera tilt and heading here keeps picking, orders and landing on the same
        /// ground the transformed pixels show.</summary>
        public Vector2 ToPlan(Vector2 screen)
        {
            var local = RotateForHeading(
                RemoveTilt(screen - SheetCenter, _sheetTilt), -_sheetHeading) / _sheetScale;
            return new Vector2(local.x / TurfPlate.S + TurfPlate.AW * 0.5f,
                local.y / TurfPlate.S + TurfPlate.AH * 0.5f);
        }

        /// <summary>The same conversion the other way, for the labels that float over
        /// the plate as real type rather than as pixels.</summary>
        public Vector2 ToScreen(Vector2 plan)
        {
            var local = new Vector2(
                (plan.x - TurfPlate.AW * 0.5f) * TurfPlate.S,
                (plan.y - TurfPlate.AH * 0.5f) * TurfPlate.S) * _sheetScale;
            return SheetCenter + ApplyTilt(
                RotateForHeading(local, _sheetHeading), _sheetTilt);
        }

        /// <summary>World metres straight to the screen, for the chrome that labels
        /// ground rather than pixels.</summary>
        public Vector2 WorldToScreen(Vector2 worldXZ) =>
            ToScreen(_survey.Plan.ToPlan(worldXZ));

        void PushStatic()
        {
            (TurfOn ? _survey.Composite : _survey.Plain).Apply(_groundTex);
            _staticUploads++;
        }

        /// <summary>A cheap fingerprint of who holds what. Ownership changes are rare
        /// and redrawing the plate is not - so the plate is redrawn when this number
        /// moves and never on a timer. Shared with the corner minimap, which has the
        /// same question and must not grow a second answer to it.</summary>
        internal static int OwnershipStamp(RoadDemoBuilder builder = null)
        {
            int stamp = 17;
            var held = PropertyRegistry.Businesses;
            for (int i = 0; i < held.Count; i++)
                stamp = stamp * 31 + (held[i] != null ? held[i].GangId + 2 : 0);
            if (builder != null)
                stamp = stamp * 31 + builder.Territories.StateStamp;
            return stamp;
        }

        public void SetTurf(bool on)
        {
            if (TurfOn == on) return;
            TurfOn = on;
            PushStatic();
        }

        // ------------------------------------------------------------------- crews

        /// <summary>
        /// Every crew in the street, as the map knows them. Refreshed rather than
        /// rebuilt where possible: a crew's dossier is its lieutenant's, and re-reading
        /// the roster every frame to print four stars would be a waste of a book.
        /// </summary>
        void CollectCrews(bool preserveContext = false)
        {
            bool preserveCrewFile = preserveContext && _crewFileRequested;
            int inspectedId = preserveCrewFile && _inspectedCrew != null
                ? _inspectedCrew.Id : -1;
            HashSet<int> gathered = preserveContext
                ? new HashSet<int>(_selected) : null;

            _units.Clear();
            _selected.Clear();
            _inspectedCrew = null;
            _crewFileRequested = false;
            if (_crews == null)
                return;

            var roster = PersonnelDirector.Instance != null ? PersonnelDirector.Instance.Roster : null;

            foreach (var unit in _crews.Units)
            {
                if (!EligibleCrewUnit(unit, roster))
                    continue;

                var crew = new TurfCrew
                {
                    Unit = unit,
                    Id = unit.CrewId,
                    GangId = unit.Faction,
                    Mine = unit.Faction == 0,
                    Post = unit.Position,
                };

                // A man's rank is the ledger's word, never the crew's size: a rival's
                // men are on nobody's books and print the plain fact of who leads.
                foreach (var walker in unit.All())
                {
                    if (walker == null)
                        continue;
                    var character = Man(roster, walker);
                    crew.Men.Add(new TurfMan
                    {
                        Name = string.IsNullOrEmpty(walker.DisplayName) ? "Unnamed" : walker.DisplayName,
                        Role = character != null
                            ? character.Rank.ToString().ToUpperInvariant()
                            : walker == unit.Boss ? "LIEUTENANT" : "MUSCLE",
                        Gun = GunName(walker.WeaponKind),
                        Condition = walker.Health >= 3 ? "FIT" : walker.Health == 2 ? "WINGED" : "HURT",
                        ConditionNote = walker.Health >= 3 ? "on his feet"
                            : walker.Health == 2 ? "walking it off" : "patched up at home",
                    });
                }

                var leader = Man(roster, unit.Boss);
                crew.Rank = leader != null ? leader.Rank.ToString().ToUpperInvariant() : "";
                crew.Name = crew.Men.Count > 0 ? crew.Men[0].Name : unit.Name;
                crew.Ride = unit.Car != null ? unit.Car.DisplayName : "On foot";
                crew.Gun = unit.Boss != null ? GunName(unit.Boss.WeaponKind) : "Bare hands";
                crew.Loyal = unit.Loyalty;
                crew.Zone = new Rect(unit.Position.x - ZoneWide * 0.5f,
                    unit.Position.z - ZoneDeep * 0.5f, ZoneWide, ZoneDeep);

                ReadDossier(crew, roster);
                _units.Add(crew);
            }

            _seenPersonnelVersion = PersonnelDirector.Instance != null
                ? PersonnelDirector.Instance.Version : -1;

            if (preserveContext)
            {
                foreach (var crew in _units)
                {
                    if (preserveCrewFile && crew.Id == inspectedId)
                    {
                        _inspectedCrew = crew;
                        _crewFileRequested = true;
                    }
                    if (gathered.Contains(crew.Id) && crew.Mine && crew.Alive)
                        _selected.Add(crew.Id);
                }
                return;
            }

            // The map opens on whoever the street had picked. The traffic goes the other
            // way on every click up here, so it has to go this way once at the door or
            // the wheel would silently drop a selection every time it crossed the line.
            var standing = _crews.Selected;
            if (standing == null)
                return;
            foreach (var crew in _units)
                if (crew.Unit == standing && crew.Mine && crew.Alive)
                    _selected.Add(crew.Id);
        }

        /// <summary>
        /// The lieutenant's file, off the outfit's own roster. The map prints the
        /// same Intelligence, Organization and Firearms ratings the personnel ledger
        /// owns; it does not translate them into map-only stats.
        /// </summary>
        static void ReadDossier(TurfCrew crew, LivingCity.Personnel.Roster roster)
        {
            if (roster == null)
                return;

            var book = roster.FindCrew(crew.Id);
            if (book == null)
                return;

            var lieutenant = roster.Find(book.LieutenantId);
            if (lieutenant == null)
                return;

            crew.Book = book;
            crew.Lieutenant = lieutenant;
            crew.Name = lieutenant.FullName;
            crew.Rank = lieutenant.Rank.ToString().ToUpperInvariant();
            crew.Intelligence = Stars(lieutenant,
                LivingCity.Personnel.CharacterAttribute.Intelligence);
            crew.Organization = Stars(lieutenant,
                LivingCity.Personnel.CharacterAttribute.Organization);
            crew.Firearms = Stars(lieutenant,
                LivingCity.Personnel.CharacterAttribute.Firearms);
            crew.Loyal = lieutenant.Loyalty;
            crew.Gun = LedgerGun(roster, lieutenant);
        }

        /// <summary>Keeps an already-open file tied to the live street unit and the
        /// versioned personnel book. The panel is persistent, so opening it once must
        /// not freeze a name, face, weapon, ride or rating into a stale snapshot.</summary>
        void RefreshCrewDossiers()
        {
            bool repaint = false;
            if (CrewShapeChanged())
            {
                // PersonnelDirector.Start and DemoCrews.Update have no guaranteed order:
                // on the first frame the persistent panel can collect an empty street,
                // then never see the lieutenant dealt one Update later. A newspaper hire
                // is the same shape change later in the game. Rebuild only when the live
                // set changes, preserving the file and gathered crews already in use.
                CollectCrews(preserveContext: true);
                _mapChrome.SelectionChanged();
                repaint = true;
            }

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            bool booksChanged = director != null && director.Version != _seenPersonnelVersion;
            repaint |= booksChanged;

            foreach (var crew in _units)
            {
                if (crew == null || crew.Unit == null)
                    continue;

                string ride = crew.Unit.Car != null ? crew.Unit.Car.DisplayName : "On foot";
                string gun = crew.Mine && crew.Lieutenant != null
                    ? LedgerGun(roster, crew.Lieutenant)
                    : crew.Unit.Boss != null ? GunName(crew.Unit.Boss.WeaponKind) : "Bare hands";
                if (crew.Ride != ride || crew.Gun != gun)
                {
                    crew.Ride = ride;
                    crew.Gun = gun;
                    repaint = true;
                }

                if (booksChanged && crew.Mine)
                    ReadDossier(crew, roster);
            }

            if (director != null)
                _seenPersonnelVersion = director.Version;
            if (repaint)
                _crewPanel.SelectionChanged();
        }

        /// <summary>Whether the panel's crew rows describe the same live units as the
        /// street. Membership changes inside a crew do not rebuild the list; a hired,
        /// dead or newly dealt lieutenant does.</summary>
        bool CrewShapeChanged()
        {
            if (_crews == null)
                return _units.Count != 0;

            var roster = PersonnelDirector.Instance != null
                ? PersonnelDirector.Instance.Roster : null;
            int eligible = 0;
            foreach (var unit in _crews.Units)
            {
                if (!EligibleCrewUnit(unit, roster))
                    continue;
                eligible++;

                bool known = false;
                foreach (var crew in _units)
                    if (crew != null && crew.Unit == unit)
                    {
                        known = true;
                        break;
                    }
                if (!known)
                    return true;
            }
            return eligible != _units.Count;
        }

        /// <summary>Our rows appear only after the book and the street agree on the
        /// lieutenant. This removes the one-frame synthetic name/portrait that used to
        /// be printed while Start order was still unresolved.</summary>
        static bool EligibleCrewUnit(DemoCrews.Unit unit,
            LivingCity.Personnel.Roster roster)
        {
            if (unit == null || unit.IsPolice || unit.Wiped || unit.Boss == null)
                return false;
            if (unit.Faction != 0)
                return true;
            if (roster == null)
                return false;

            var book = roster.FindCrew(unit.CrewId);
            var lieutenant = book != null ? roster.Find(book.LieutenantId) : null;
            return lieutenant != null && unit.Boss.CharacterId == lieutenant.Id;
        }

        /// <summary>The same firearm answer used to arm the street: the exact item
        /// assigned to this man, or the personal .38 that every ordinary outfit man
        /// carries when no stock weapon is signed out to him.</summary>
        static string LedgerGun(LivingCity.Personnel.Roster roster,
            LivingCity.Personnel.Character man)
        {
            var item = man != null ? CrewArms.FirearmOf(roster, man.Id) : null;
            if (item == null)
                return "Revolver, .38 — his own";
            return !string.IsNullOrEmpty(item.DisplayName)
                ? item.DisplayName
                : LivingCity.UI.LedgerText.EquipmentLabel(item.Kind);
        }

        /// <summary>The Personal File's plus and minus are only views onto the shared
        /// personnel doors. Recruit pays and respects Crew.MaxHoods; release returns a
        /// hood to the pool instead of deleting him.</summary>
        public void RecruitHood(TurfCrew crew)
        {
            if (_crews != null && crew != null && _crews.Recruit(crew.Unit))
                _seenPersonnelVersion = -1;
        }

        public void ReleaseHood(TurfCrew crew)
        {
            if (_crews != null && crew != null && _crews.ReleaseHood(crew.Unit))
                _seenPersonnelVersion = -1;
        }

        /// <summary>The ledger entry behind a man on the street. Rivals are on
        /// nobody's books and answer null, which is why every read of this is
        /// guarded.</summary>
        static LivingCity.Personnel.Character Man(LivingCity.Personnel.Roster roster,
            CrewWalker walker) =>
            roster != null && walker != null && walker.CharacterId >= 0
                ? roster.Find(walker.CharacterId)
                : null;

        static int Stars(LivingCity.Personnel.Character man,
            LivingCity.Personnel.CharacterAttribute attribute) =>
            Mathf.Clamp(man.GetHalfSteps(attribute), 0, 10);

        /// <summary>What the armoury calls the gun in a man's hands. The catalog is
        /// the game's own word for it, so the dossier and the ledger's armoury page
        /// never disagree about what a lieutenant is carrying.</summary>
        static string GunName(LivingCity.Personnel.EquipmentKind kind)
        {
            foreach (var item in LivingCity.Outfit.ArmoryCatalog.Weapons)
                if (item.Kind == kind)
                    return item.DisplayName;
            if (kind == LivingCity.Personnel.EquipmentKind.Pistol)
                return "Revolver, .38";
            return kind == LivingCity.Personnel.EquipmentKind.Grenade ? "Grenade" : "Bare hands";
        }

        // ------------------------------------------------------------------ pointer

        void Pointer()
        {
            if (_rig != null && _rig.SuppressInput)
                return;
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var screen = mouse.position.ReadValue();
            bool overChrome = _mapChrome.ClaimsPointer(screen) ||
                              _crewPanel.ClaimsPointer(screen) ||
                              (EventSystem.current && EventSystem.current.IsPointerOverGameObject());
            PointerOverChrome = overChrome;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // Same dismissal rule as the street card: either mouse button outside
                // answers the open question first and does not also issue another order.
                if (_mapChrome.MenuOpen)
                {
                    _mapChrome.CloseMenu();
                    return;
                }
                if (overChrome)
                    return;

                var plan = ToPlan(screen);

                // A rival answers with the exact street choices. The map owns the paper
                // they are drawn on, but CrewOverlay owns what KILL / DRIVE-BY / BOMBA do.
                var target = NearestCrew(plan, false);
                if (target != null && !target.Mine)
                {
                    if (_crewOverlay == null && _crews != null)
                        _crewOverlay = _crews.GetComponent<CrewOverlay>();
                    if (_enemyActions == null)
                        _enemyActions = new List<CrewEnemyAction>();
                    if (_crewOverlay != null &&
                        _crewOverlay.TryGetEnemyActions(target.Unit, _enemyActions))
                        _mapChrome.OpenEnemyMenu(
                            screen, _crews.Selected, target.Unit, _enemyActions);
                    return;
                }

                if (_selected.Count == 0 || _crews == null)
                    return;

                // CrewOverlay's gesture is authored on a 1080-line canvas. TurfMap's
                // furniture uses a 720-line canvas, so using its scale factor here would
                // make the same double click fifty percent looser on the map.
                float slack = DoubleRightSlack * Mathf.Max(0.01f, Screen.height / 1080f);
                bool run = Time.unscaledTime - _lastRightOrderAt <= DoubleRightClick &&
                           (screen - _lastRightOrderScreen).sqrMagnitude <= slack * slack;
                _lastRightOrderAt = Time.unscaledTime;
                _lastRightOrderScreen = screen;
                MoveHere(plan, run);
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (overChrome)
                    return;
                if (_mapChrome.MenuOpen)
                {
                    _mapChrome.CloseMenu();
                    return;
                }
                _dragging = true;
                _dragMoved = false;
                _dragFrom = _dragTo = _survey.Plan.ToWorld(ToPlan(screen));
                return;
            }

            if (_dragging)
            {
                _dragTo = _survey.Plan.ToWorld(ToPlan(screen));
                var from = _survey.Plan.ToPlan(_dragFrom);
                var to = _survey.Plan.ToPlan(_dragTo);
                if (Mathf.Abs(to.x - from.x) > DragSlop || Mathf.Abs(to.y - from.y) > DragSlop)
                    _dragMoved = true;
            }

            if (!mouse.leftButton.wasReleasedThisFrame || !_dragging)
                return;

            _dragging = false;
            if (_dragMoved)
            {
                Marquee();
                return;
            }

            Click(ToPlan(screen));
        }

        /// <summary>A dragged box takes every crew of OURS inside it and nobody
        /// else's. A rival cannot be gathered, so a box thrown over a street brawl
        /// selects our men out of it rather than both sides.</summary>
        void Marquee()
        {
            var box = Rect.MinMaxRect(
                Mathf.Min(_dragFrom.x, _dragTo.x), Mathf.Min(_dragFrom.y, _dragTo.y),
                Mathf.Max(_dragFrom.x, _dragTo.x), Mathf.Max(_dragFrom.y, _dragTo.y));

            _selected.Clear();
            foreach (var crew in _units)
                if (crew.Mine && crew.Alive &&
                    box.Contains(new Vector2(crew.Unit.Position.x, crew.Unit.Position.z)))
                    _selected.Add(crew.Id);

            _inspectedBuilding = null;
            _inspectedDistrict = null;
            _inspectedCrew = null;
            _crewFileRequested = false;
            Changed();
        }

        /// <summary>One click, in the design's priority order: our crew, then anyone
        /// else's, then a footprint, then the ground it stands on.</summary>
        void Click(Vector2 plan)
        {
            var mine = NearestCrew(plan, true);
            if (mine != null)
            {
                _selected.Clear();
                _selected.Add(mine.Id);
                Inspect(mine);
                return;
            }

            var other = NearestCrew(plan, false);
            if (other != null)
            {
                _selected.Clear();
                Inspect(other);
                return;
            }

            var building = _survey.BuildingAt(plan);
            if (building != null)
            {
                _inspectedCrew = null;
                _crewFileRequested = false;
                _inspectedDistrict = null;
                _inspectedBuilding = building;
                Changed();
                return;
            }

            _selected.Clear();
            _inspectedCrew = null;
            _crewFileRequested = false;
            _inspectedBuilding = null;
            _inspectedDistrict = _survey.DistrictAtPlan(plan);
            Changed();
        }

        TurfCrew NearestCrew(Vector2 plan, bool oursOnly)
        {
            TurfCrew best = null;
            float bestSqr = PickRadius * PickRadius;
            foreach (var crew in _units)
            {
                if (!crew.Alive || (oursOnly && !crew.Mine))
                    continue;
                float sqr = (crew.Plan - plan).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                bestSqr = sqr;
                best = crew;
            }
            return best;
        }

        internal bool EnemyContextValid(DemoCrews.Unit actor, DemoCrews.Unit target) =>
            _crews != null && _crews.Selected == actor && actor != null && !actor.Wiped &&
            target != null && target.Faction != 0 && !target.Wiped;

        public void Inspect(TurfCrew crew)
        {
            _inspectedBuilding = null;
            _inspectedDistrict = null;
            _inspectedCrew = crew;
            _crewFileRequested = crew != null;
            Changed();
        }

        public void SelectOnly(TurfCrew crew)
        {
            _selected.Clear();
            if (crew != null && crew.Mine)
                _selected.Add(crew.Id);
            Inspect(crew);
        }

        /// <summary>Put the shared camera on this lieutenant without changing the map's
        /// selection or zoom. Used by the roster's right click at every zoom level.</summary>
        public void Focus(TurfCrew crew)
        {
            if (_rig == null || crew == null || !crew.Alive || crew.Unit == null)
                return;
            _rig.Ride(crew.Unit);
        }

        public void GatherAll()
        {
            _selected.Clear();
            foreach (var crew in _units)
                if (crew.Mine && crew.Alive)
                    _selected.Add(crew.Id);
            Changed();
        }

        /// <summary>
        /// Every path that moves the map's selection ends here, and there is exactly one
        /// of these so that the two things a selection owes get done together: the panel
        /// repaints, and THE STREET IS TOLD.
        ///
        /// The street's own selection is a single crew - it is what a right-click in the
        /// city orders and what the crew bar rims - so it follows the lieutenant whose
        /// file is open, or the first of a gathered lot if no file is. Picking a name off
        /// the roster up here and finding nobody picked when the wheel comes back down is
        /// two selections for one game.
        /// </summary>
        void Changed()
        {
            _mapChrome.SelectionChanged();
            _crewPanel.SelectionChanged();
            if (_crews == null)
                return;

            var inspected = InspectedCrew;
            var pick = inspected != null && inspected.Mine && inspected.Alive
                ? inspected
                : FirstGathered();
            _crews.Select(pick != null ? pick.Unit : null);
        }

        TurfCrew FirstGathered()
        {
            foreach (var crew in _units)
                if (crew.Mine && crew.Alive && _selected.Contains(crew.Id))
                    return crew;
            return null;
        }

        /// <summary>Open a footprint's file without disturbing who is gathered - the
        /// menu's "read" verbs, which are a look and not an order.</summary>
        public void ReadProperty(TurfBuilding building)
        {
            _inspectedCrew = null;
            _crewFileRequested = false;
            _inspectedDistrict = null;
            _inspectedBuilding = building;
            Changed();
        }

        public void ReadDistrict(TurfDistrict district)
        {
            _inspectedCrew = null;
            _crewFileRequested = false;
            _inspectedBuilding = null;
            _inspectedDistrict = district;
            Changed();
        }

        public void ClearInspection()
        {
            _inspectedCrew = null;
            _crewFileRequested = false;
            _inspectedBuilding = null;
            _inspectedDistrict = null;
            _selected.Clear();
            Changed();
        }

        // ------------------------------------------------------------------ orders

        /// <summary>A bare TurfMap right click is the street's ordinary move command,
        /// issued immediately. An explicit unit path preserves driving/boarding behavior;
        /// a gathered group simply receives that same command once per crew.</summary>
        void MoveHere(Vector2 plan, bool run)
        {
            if (_selected.Count == 0 || _crews == null)
                return;

            var at = _survey.Plan.ToWorld(plan);
            int live = 0;
            foreach (var crew in _units)
                if (crew.Mine && crew.Alive && _selected.Contains(crew.Id))
                    live++;

            var scatter = new TurfPlate.Roll(Time.frameCount);
            bool moved = false;
            foreach (var crew in _units)
            {
                if (!crew.Mine || !crew.Alive || !_selected.Contains(crew.Id))
                    continue;

                var destination = live > 1 ? Spread(at, ref scatter) : at;
                var world = new Vector3(destination.x, _crews.GroundY, destination.y);
                if (!_crews.OrderUnit(crew.Unit, world, out _, run))
                    continue;

                crew.Order = TurfOrder.Moving;
                crew.Taking = null;
                moved = true;
            }

            if (!moved)
                return;

            _markers.Add(new Marker
            {
                World = at,
                Life = MarkerSeconds,
                Order = TurfOrder.Moving,
            });
            Changed();
        }

        /// <summary>
        /// Every order the map can give, issued to the whole selection. Nothing here
        /// moves a man: the order is handed to DemoCrews, which already knows how to
        /// walk a crew down a pavement, board it into a car and set it on somebody -
        /// and which would undo anything this map did behind its back.
        ///
        /// The target arrives in authored units, because that is what a click is, and
        /// is turned into GROUND on the first line. Everything downstream of here is in
        /// metres: an order outlives the projection it was given under.
        /// </summary>
        public void Order(TurfOrder order, Vector2 plan, TurfBuilding building)
        {
            if (_selected.Count == 0 || _crews == null)
                return;

            var at = _survey.Plan.ToWorld(plan);
            var scatter = new TurfPlate.Roll(Time.frameCount);

            foreach (var crew in _units)
            {
                if (!crew.Mine || !crew.Alive || !_selected.Contains(crew.Id))
                    continue;

                crew.Order = order;
                crew.Taking = null;

                switch (order)
                {
                    case TurfOrder.Moving:
                    case TurfOrder.WalkingIn:
                        // targets are scattered so a gathered lot do not all walk to
                        // the same paving stone and shove each other off it
                        March(crew, Spread(at, ref scatter), order == TurfOrder.WalkingIn);
                        break;

                    case TurfOrder.Taking:
                        crew.Taking = building;
                        if (building != null)
                            March(crew, building.World.center, true);
                        break;

                    case TurfOrder.Walking:
                        crew.Zone = new Rect(at.x - ZoneWide * 0.5f, at.y - ZoneDeep * 0.5f,
                            ZoneWide, ZoneDeep);
                        crew.Wander = Spread(at, ref scatter);
                        March(crew, crew.Wander, false);
                        break;

                    case TurfOrder.Holding:
                        March(crew, Flat(crew.Unit.Position), false);
                        break;

                    case TurfOrder.PullingBack:
                        March(crew, Flat(crew.Post), false);
                        break;

                    case TurfOrder.ToTheOutfit:
                        SendHome(crew);
                        break;

                    case TurfOrder.InTheCar:
                        Board(crew);
                        break;
                }
            }

            _markers.Add(new Marker { World = at, Life = MarkerSeconds, Order = order });
            Changed();
        }

        static Vector2 Flat(Vector3 world) => new Vector2(world.x, world.z);

        /// <summary>Fifteen metres of slop about a target - a crew is several men wide
        /// and they cannot all stand on the same flagstone.</summary>
        static Vector2 Spread(Vector2 world, ref TurfPlate.Roll roll) =>
            world + new Vector2(roll.Next() - 0.5f, roll.Next() - 0.5f) * 15f;

        void March(TurfCrew crew, Vector2 world, bool run)
        {
            _crews.MarchTo(crew.Unit, new Vector3(world.x, _crews.GroundY, world.y), run);
        }

        void SendHome(TurfCrew crew)
        {
            var outfit = OutfitDirector.Instance;
            if (outfit != null && outfit.TryGetHeadquarters(out var hq, out _))
            {
                _crews.MarchTo(crew.Unit, hq);
                return;
            }
            _crews.MarchTo(crew.Unit, crew.Post);
        }

        /// <summary>Back into the car - the nearest of the outfit's own, and only if
        /// there is one within a walk. A crew told to board a car three districts away
        /// would simply walk off the job.</summary>
        void Board(TurfCrew crew)
        {
            CrewCar best = null;
            float bestSqr = 120f * 120f;
            foreach (var car in _crews.Cars)
            {
                if (car == null || car.Tf == null)
                    continue;
                float sqr = (car.Tf.position - crew.Unit.Position).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                bestSqr = sqr;
                best = car;
            }

            if (best != null)
                _crews.BoardCar(crew.Unit, best);
            else
                crew.Order = TurfOrder.Holding;
        }

        /// <summary>
        /// What an order does AFTER it is given: a patrol that reached its waypoint
        /// picks another inside its box, an assault that arrived looks for somebody to
        /// hit, and a claim that arrived is put to the claim rule.
        /// </summary>
        void Steer()
        {
            var roll = new TurfPlate.Roll(Time.frameCount * 9176 + 1987);

            foreach (var crew in _units)
            {
                if (!crew.Alive)
                    continue;

                crew.Plan = _survey.Plan.ToPlan(crew.Unit.Position);
                if (!crew.Mine)
                    continue;

                switch (crew.Order)
                {
                    case TurfOrder.Walking:
                        if (Arrived(crew, crew.Wander))
                        {
                            crew.Wander = new Vector2(
                                crew.Zone.xMin + roll.Next() * crew.Zone.width,
                                crew.Zone.yMin + roll.Next() * crew.Zone.height);
                            March(crew, crew.Wander, false);
                        }
                        break;

                    case TurfOrder.WalkingIn:
                        Sic(crew);
                        break;

                    case TurfOrder.Taking:
                        if (crew.Taking != null && Arrived(crew, crew.Taking.World.center))
                            Claim(crew);
                        break;
                }
            }
        }

        bool Arrived(TurfCrew crew, Vector2 world)
        {
            var to = crew.Unit.Position -
                     new Vector3(world.x, crew.Unit.Position.y, world.y);
            return to.sqrMagnitude < ArriveMetres * ArriveMetres;
        }

        /// <summary>An assault sets the crew on the nearest rival once they are close
        /// enough to see one - a block, the same reach the order book's own jobs use.
        /// </summary>
        void Sic(TurfCrew crew)
        {
            DemoCrews.Unit best = null;
            float bestSqr = CrewJobs.MarkRadius * CrewJobs.MarkRadius;
            foreach (var other in _crews.Units)
            {
                if (other == null || other.Faction == 0 || other.IsPolice || other.Wiped)
                    continue;
                float sqr = (other.Position - crew.Unit.Position).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                bestSqr = sqr;
                best = other;
            }

            if (best != null)
                _crews.Sic(crew.Unit, best);
        }

        void Claim(TurfCrew crew)
        {
            var building = crew.Taking;
            crew.Taking = null;
            crew.Order = TurfOrder.Holding;

            if (building == null || building.Business == null)
                return;

            var rule = ClaimRule ?? DefaultClaim;
            if (!rule(building, crew))
                return;

            building.Business.GangId = crew.GangId;

            // The wash and the footprints are now wrong on a plate nobody is going to
            // redraw for zoom reasons - the boom has not moved.
            _ownershipStale = true;
        }

        /// <summary>The stub rule: the crew is standing on it and nobody else's men
        /// are. Replaced wholesale by assigning <see cref="ClaimRule"/>.</summary>
        bool DefaultClaim(TurfBuilding building, TurfCrew crew)
        {
            foreach (var other in _units)
            {
                if (other.Mine || !other.Alive)
                    continue;
                if ((other.Plan - crew.Plan).sqrMagnitude < PickRadius * PickRadius * 4f)
                    return false;
            }
            return true;
        }

        // -------------------------------------------------------------- the moving

        /// <summary>
        /// The one layer redrawn per frame, in the design's order: traffic, I-key
        /// movement routes, then crews with their glow, order markers and the marquee.
        /// Buildings and the wash are NOT here - they are stacked underneath as their
        /// own textures, which is what keeps this to a clear and a few thousand pixels.
        /// </summary>
        void DrawLive()
        {
            _live.Clear(new Color32(0, 0, 0, 0));

            DrawTraffic();
            DrawPickedBuilding();
            DrawMovementIndicators();
            DrawCrews();
            DrawMarkers();
            DrawMarquee();

            _live.Apply(_liveTex);
        }

        void DrawTraffic()
        {
            if (_traffic != null)
                foreach (var car in _traffic)
                    DrawCar(car, TurfInk.Civilian, false);

            if (_policeCars != null)
                foreach (var car in _policeCars)
                    DrawCar(car, TurfInk.Ink, false);

            if (_crews != null)
                foreach (var car in _crews.Cars)
                {
                    if (car == null || car.Owner == null)
                        continue;
                    DrawCar(car, TurfHouses.For(car.Owner.Faction).Ink, false);
                }
        }

        /// <summary>
        /// A vehicle: a body that narrows at both ends, a dark cabin, and one pale
        /// lamp at the end it is travelling toward. Shape tells a car from a man on
        /// this map - never colour, because colour already means a family.
        /// </summary>
        void DrawCar(RoadCar car, Color32 ink, bool big)
        {
            if (car == null || car.Tf == null)
                return;

            var plan = _survey.Plan.ToPlan(car.Tf.position);
            int rx = Mathf.RoundToInt(plan.x * TurfPlate.S);
            int ry = Mathf.RoundToInt(plan.y * TurfPlate.S);
            if (rx < -16 || ry < -16 || rx > TurfPlate.RW + 16 || ry > TurfPlate.RH + 16)
                return;

            var forward = car.Tf.forward;
            bool vertical = Mathf.Abs(forward.z) > Mathf.Abs(forward.x);
            bool ahead = vertical ? forward.z > 0f : forward.x > 0f;

            int length = big ? 12 : 8, thick = 4;
            if (vertical)
            {
                _live.Px(rx, ry + 1, thick, length - 2, ink);
                _live.Px(rx + 1, ry, thick - 2, length, ink);
                _live.Px(rx + 1, ry + 2, thick - 2, length - 4, TurfInk.Cabin);
                _live.Px(rx + 1, ry + (ahead ? length - 2 : 1), thick - 2, 1, TurfInk.Lamp);
            }
            else
            {
                _live.Px(rx + 1, ry, length - 2, thick, ink);
                _live.Px(rx, ry + 1, length, thick - 2, ink);
                _live.Px(rx + 2, ry + 1, length - 4, thick - 2, TurfInk.Cabin);
                _live.Px(rx + (ahead ? length - 2 : 1), ry + 1, 1, thick - 2, TurfInk.Lamp);
            }
        }

        // ------------------------------------------------------- movement intent

        /// <summary>The world I-key view fans a route out to every formation position.
        /// On the TurfMap a dot already means the whole crew, so its movement is reduced
        /// to the leader's real planned way and one shared destination mark. A crew in a
        /// car similarly contributes one car route, never one line per passenger.</summary>
        void DrawMovementIndicators()
        {
            if (_crews == null || _crews.IntentOverlay == null ||
                !_crews.IntentOverlay.IsVisible)
                return;

            foreach (var crew in _units)
            {
                if (crew == null || !crew.Mine || !crew.Alive || crew.Unit == null)
                    continue;

                var unit = crew.Unit;
                if (unit.Car != null && unit.Car.Occupant == unit &&
                    unit.Car.CopyPlannedRoute(_movementPath) && _movementPath.Count > 1)
                {
                    DrawMovementRoute(_movementPath, TurfInk.MovementDrive);
                    continue;
                }

                var lead = CrewMoveLeader(unit);
                if (!MovingOnFoot(lead) || !lead.CopyPlannedRoute(_movementPath) ||
                    _movementPath.Count < 2)
                    continue;

                DrawMovementRoute(_movementPath,
                    lead.Urgent ? TurfInk.MovementRun : TurfInk.MovementWalk);
            }
        }

        /// <summary>The crew-level position is its lieutenant, or the first man still
        /// standing when he is down. A late hood finishing his formation leg does not
        /// grow a second map route after that shared point has already arrived.</summary>
        static CrewWalker CrewMoveLeader(DemoCrews.Unit unit)
        {
            if (unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null &&
                !unit.Boss.Riding)
                return unit.Boss;

            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null && !man.Riding)
                    return man;
            return null;
        }

        static bool MovingOnFoot(CrewWalker man) =>
            man != null && !man.Dead && man.Tf != null && !man.Riding &&
            (man.State == CrewWalker.Mode.Walking ||
             man.State == CrewWalker.Mode.Homing ||
             man.State == CrewWalker.Mode.Striding);

        void DrawMovementRoute(List<Vector3> worldPath, Color32 colour)
        {
            int weight = Mathf.Max(1, Mathf.RoundToInt(2f * _indicatorScale));
            Vector2 previous = RoutePixel(worldPath[0]);
            for (int i = 1; i < worldPath.Count; i++)
            {
                Vector2 next = RoutePixel(worldPath[i]);
                DrawRouteLeg(previous, next, weight, colour);
                previous = next;
            }

            // One target for the crew, regardless of how many men will fan out around
            // it in the 3D formation view.
            int cx = Mathf.RoundToInt(previous.x);
            int cy = Mathf.RoundToInt(previous.y);
            int reach = Mathf.Max(2, Mathf.RoundToInt(4f * _indicatorScale));
            _live.Px(cx - reach, cy - reach, reach * 2 + 1, weight, colour);
            _live.Px(cx - reach, cy + reach, reach * 2 + 1, weight, colour);
            _live.Px(cx - reach, cy - reach, weight, reach * 2 + 1, colour);
            _live.Px(cx + reach, cy - reach, weight, reach * 2 + 1, colour);
        }

        Vector2 RoutePixel(Vector3 world)
        {
            var plan = _survey.Plan.ToPlan(world);
            return plan * TurfPlate.S;
        }

        /// <summary>Integer survey line with clipping. At a close map zoom most of a
        /// long route can be thousands of pixels beyond the plate; clipping before the
        /// Bresenham walk keeps that invisible distance out of the per-frame cost.</summary>
        void DrawRouteLeg(Vector2 from, Vector2 to, int weight, Color32 colour)
        {
            if (!ClipRouteLeg(ref from, ref to))
                return;

            int x0 = Mathf.RoundToInt(from.x), y0 = Mathf.RoundToInt(from.y);
            int x1 = Mathf.RoundToInt(to.x), y1 = Mathf.RoundToInt(to.y);
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            int offset = weight / 2;

            while (true)
            {
                _live.Px(x0 - offset, y0 - offset, weight, weight, colour);
                if (x0 == x1 && y0 == y1)
                    break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }

        static bool ClipRouteLeg(ref Vector2 from, ref Vector2 to)
        {
            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float enter = 0f, leave = 1f;
            if (!ClipRouteEdge(-dx, from.x, ref enter, ref leave) ||
                !ClipRouteEdge(dx, TurfPlate.RW - 1f - from.x, ref enter, ref leave) ||
                !ClipRouteEdge(-dy, from.y, ref enter, ref leave) ||
                !ClipRouteEdge(dy, TurfPlate.RH - 1f - from.y, ref enter, ref leave))
                return false;

            var start = from;
            if (leave < 1f)
                to = start + new Vector2(dx, dy) * leave;
            if (enter > 0f)
                from = start + new Vector2(dx, dy) * enter;
            return true;
        }

        static bool ClipRouteEdge(float direction, float distance,
            ref float enter, ref float leave)
        {
            if (Mathf.Approximately(direction, 0f))
                return distance >= 0f;

            float ratio = distance / direction;
            if (direction < 0f)
            {
                if (ratio > leave) return false;
                if (ratio > enter) enter = ratio;
            }
            else
            {
                if (ratio < enter) return false;
                if (ratio < leave) leave = ratio;
            }
            return true;
        }

        // ------------------------------------------------------------- the crew dot

        /// <summary>
        /// The crew marker, in AUTHORED UNITS as the design sets it: a core of 2.4, a
        /// pale pip of 1.5 inside it, four corner brackets at 6 when it is gathered,
        /// and a glow sixteen raster pixels across.
        ///
        /// Units and not pixels, and that distinction is the whole of what was wrong
        /// here: these were being handed to a raster routine as though they were pixel
        /// counts, so every marker on the map came out at a third of the size the
        /// design draws it and a crew was a speck the mouse could hardly find.
        /// </summary>
        const float CoreUnits = 2.4f, PipUnits = 1.5f, BracketUnits = 6f;

        /// <summary>The glow's reach in raster pixels - the design's 32 x 32 sprite.
        /// </summary>
        const int GlowRadius = 16;

        /// <summary>The glow's falloff, worked out once: alpha full at the centre, 0.45
        /// of it at 45% of the way out, nothing at the edge. Per pixel this used to be
        /// a square root and a pair of lerps, for every crew, every frame.</summary>
        static byte[] _glowAlpha, _glowMix;

        static void BankGlow()
        {
            if (_glowAlpha != null)
                return;

            int span = GlowRadius * 2 + 1;
            _glowAlpha = new byte[span * span];
            _glowMix = new byte[span * span];

            for (int dy = -GlowRadius; dy <= GlowRadius; dy++)
                for (int dx = -GlowRadius; dx <= GlowRadius; dx++)
                {
                    int at = (dy + GlowRadius) * span + (dx + GlowRadius);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / GlowRadius;
                    if (d >= 1f)
                        continue;

                    float a = d < 0.45f
                        ? Mathf.Lerp(1f, 0.45f, d / 0.45f)
                        : Mathf.Lerp(0.45f, 0f, (d - 0.45f) / 0.55f);
                    _glowAlpha[at] = (byte)Mathf.RoundToInt(a * 255f);
                    _glowMix[at] = (byte)Mathf.RoundToInt(Mathf.Clamp01(d / 0.45f) * 255f);
                }
        }

        /// <summary>
        /// A crew is ONE bright dot with a glow round it, and every crew's dot is the
        /// same size. How many men a lieutenant has is read in the panel; making the
        /// dot grow with the crew turns the map into a bar chart and makes a small
        /// crew hard to click. The colour is the family's and nothing else's.
        /// </summary>
        void DrawCrews()
        {
            BankGlow();
            int glowRadius = Mathf.Max(1, Mathf.RoundToInt(GlowRadius * _indicatorScale));
            int span = GlowRadius * 2 + 1;

            foreach (var crew in _units)
            {
                if (!crew.Alive)
                    continue;

                var house = TurfHouses.For(crew.GangId);
                int cx = Mathf.RoundToInt(crew.Plan.x * TurfPlate.S);
                int cy = Mathf.RoundToInt(crew.Plan.y * TurfPlate.S);
                if (cx < -glowRadius || cy < -glowRadius ||
                    cx > TurfPlate.RW + glowRadius || cy > TurfPlate.RH + glowRadius)
                    continue;

                for (int dy = -glowRadius; dy <= glowRadius; dy++)
                    for (int dx = -glowRadius; dx <= glowRadius; dx++)
                    {
                        int sourceX = Mathf.Clamp(Mathf.RoundToInt(dx / _indicatorScale),
                            -GlowRadius, GlowRadius);
                        int sourceY = Mathf.Clamp(Mathf.RoundToInt(dy / _indicatorScale),
                            -GlowRadius, GlowRadius);
                        int at = (sourceY + GlowRadius) * span + (sourceX + GlowRadius);
                        byte alpha = _glowAlpha[at];
                        if (alpha == 0)
                            continue;

                        var tint = Color32.Lerp(house.Pencil, house.Ink, _glowMix[at] / 255f);
                        tint.a = alpha;
                        _live.OverDot(cx + dx, cy + dy, tint);
                    }

                Disc(cx, cy, CoreUnits * TurfPlate.S * _indicatorScale, house.Ink);
                Disc(cx, cy, PipUnits * TurfPlate.S * _indicatorScale, house.Pencil);

                if (_selected.Contains(crew.Id) && crew.Mine)
                    Brackets(cx, cy, _indicatorScale);

                if (InspectedCrew == crew)
                {
                    // the small cap above a crew whose file is open
                    int cap = Mathf.RoundToInt(BracketUnits * TurfPlate.S * _indicatorScale) + 2;
                    int capWidth = Mathf.Max(1, Mathf.RoundToInt(5f * _indicatorScale));
                    int capHeight = Mathf.Max(1, Mathf.RoundToInt(2f * _indicatorScale));
                    _live.Px(cx - capWidth / 2, cy + cap, capWidth, capHeight, TurfInk.Red);
                }
            }
        }

        void Disc(int cx, int cy, float radius, Color32 colour)
        {
            int r = Mathf.CeilToInt(radius);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= radius * radius)
                        _live.Dot(cx + dx, cy + dy, colour);
        }

        /// <summary>Four corner brackets - the RTS mark, in oxblood, six authored units
        /// out from the dot. Not a ring: a ring at this size is a blob, and a blob over
        /// a dot hides the dot.</summary>
        void Brackets(int cx, int cy, float scale)
        {
            int o = Mathf.Max(1, Mathf.RoundToInt(BracketUnits * TurfPlate.S * scale));
            int arm = Mathf.Max(1, Mathf.RoundToInt(6f * scale));
            int weight = Mathf.Max(1, Mathf.RoundToInt(2f * scale));
            var red = TurfInk.Red;

            _live.Px(cx - o, cy - o, arm, weight, red);
            _live.Px(cx - o, cy - o, weight, arm, red);
            _live.Px(cx + o - arm, cy - o, arm, weight, red);
            _live.Px(cx + o - weight, cy - o, weight, arm, red);
            _live.Px(cx - o, cy + o - weight, arm, weight, red);
            _live.Px(cx - o, cy + o - arm, weight, arm, red);
            _live.Px(cx + o - arm, cy + o - weight, arm, weight, red);
            _live.Px(cx + o - weight, cy + o - arm, weight, arm, red);
        }

        /// <summary>The footprint whose file is open: a two-pixel oxblood frame that
        /// breathes, the design's own selection mark. On the LIVE layer and not the
        /// built one - a blink drawn into a static plate would need the plate redrawn
        /// to blink.</summary>
        void DrawPickedBuilding()
        {
            if (_inspectedBuilding == null)
                return;

            var plan = _survey.Plan.ToPlan(_inspectedBuilding.World);
            int rx = Mathf.RoundToInt(plan.xMin * TurfPlate.S);
            int ry = Mathf.RoundToInt(plan.yMin * TurfPlate.S);
            int rw = Mathf.Max(4, Mathf.RoundToInt(plan.width * TurfPlate.S));
            int rh = Mathf.Max(4, Mathf.RoundToInt(plan.height * TurfPlate.S));
            if (rx > TurfPlate.RW || ry > TurfPlate.RH || rx + rw < 0 || ry + rh < 0)
                return;

            var red = TurfInk.Red;
            red.a = (byte)Mathf.RoundToInt(Mathf.Lerp(70f, 255f,
                Mathf.PingPong(Time.unscaledTime * 1.6f, 1f)));

            const int weight = 2;
            _live.Over(rx, ry, rw, weight, red);
            _live.Over(rx, ry + rh - weight, rw, weight, red);
            _live.Over(rx, ry, weight, rh, red);
            _live.Over(rx + rw - weight, ry, weight, rh, red);
        }

        /// <summary>Where an order landed: a square that grows and goes. Red for an
        /// assault, our own green for a claim, ink for everything else. Counted in
        /// SECONDS - a marker measured in frames is a different length of time on every
        /// machine.</summary>
        void DrawMarkers()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var marker = _markers[i];
                marker.Life -= Time.unscaledDeltaTime;
                if (marker.Life <= 0f)
                {
                    _markers.RemoveAt(i);
                    continue;
                }
                _markers[i] = marker;

                var colour = marker.Order == TurfOrder.WalkingIn ? TurfInk.Red
                    : marker.Order == TurfOrder.Taking ? TurfHouses.Ours.Ink
                    : TurfInk.Ink;

                float grown = 1f - marker.Life / MarkerSeconds;
                int size = Mathf.RoundToInt((1f + grown * 5f) * TurfPlate.S);

                var plan = _survey.Plan.ToPlan(marker.World);
                int rx = Mathf.RoundToInt(plan.x * TurfPlate.S);
                int ry = Mathf.RoundToInt(plan.y * TurfPlate.S);

                _live.Px(rx - size, ry - size, size * 2, 1, colour);
                _live.Px(rx - size, ry + size, size * 2, 1, colour);
                _live.Px(rx - size, ry - size, 1, size * 2, colour);
                _live.Px(rx + size, ry - size, 1, size * 2, colour);
            }
        }

        void DrawMarquee()
        {
            if (!_dragging || !_dragMoved)
                return;

            var from = _survey.Plan.ToPlan(_dragFrom);
            var to = _survey.Plan.ToPlan(_dragTo);

            int x0 = Mathf.RoundToInt(Mathf.Min(from.x, to.x) * TurfPlate.S);
            int y0 = Mathf.RoundToInt(Mathf.Min(from.y, to.y) * TurfPlate.S);
            int w = Mathf.RoundToInt(Mathf.Abs(to.x - from.x) * TurfPlate.S);
            int h = Mathf.RoundToInt(Mathf.Abs(to.y - from.y) * TurfPlate.S);

            for (int x = 0; x < w; x += 4)
            {
                _live.Px(x0 + x, y0, 2, 1, TurfInk.Red);
                _live.Px(x0 + x, y0 + h, 2, 1, TurfInk.Red);
            }
            for (int y = 0; y < h; y += 4)
            {
                _live.Px(x0, y0 + y, 1, 2, TurfInk.Red);
                _live.Px(x0 + w, y0 + y, 1, 2, TurfInk.Red);
            }
        }
    }
}
