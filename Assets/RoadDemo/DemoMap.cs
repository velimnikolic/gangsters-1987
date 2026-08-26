using System.Collections.Generic;
using System.Text;
using LivingCity.CameraRig;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The city's tactical map: a 1987 municipal survey terminal, rasterised at
    /// 320x200 and blown up with square pixels, with a gang-turf HUD over the top.
    ///
    /// It replaces the printed plan this class used to draw. What is kept is
    /// everything about how the map BEHAVES - it still comes up three ways and it is
    /// still the camera:
    ///
    ///   CORNER - a postcard in the bottom right holding three hundred metres of ground
    ///            under the camera's own pivot. The same raster, cropped by being drawn
    ///            at a tighter scale, with the HUD stripped off: the minimap the design
    ///            sheet says to reuse rather than build twice.
    ///
    ///   FULL   - the player pulls the wheel back past <see cref="DemoCamera.mapAt"/>
    ///            and the terminal takes the screen. The map's centre IS the camera's
    ///            pivot and its scale IS the boom, so panning the map pans the camera
    ///            and pushing the wheel in drops him back into the street he was
    ///            looking at.
    ///
    ///   DOCKED - the ledger takes the left half and the map moves into the right,
    ///            fitted to the whole city.
    ///
    /// WHAT IS DRAWN AND WHERE. The raster is exactly 320x200, always, whatever the
    /// display is. Nothing is antialiased and nothing is drawn at a fractional
    /// coverage; the blow-up is a point-filtered RawImage. The HUD - masthead, rail,
    /// footer, district lettering, scanlines - is uGUI OVER that image and never inside
    /// it, which is what keeps the chrome readable while the picture stays coarse.
    ///
    /// WHAT COSTS WHAT. The ground, the buildings and the turf wash are three cached
    /// buffers, re-rasterised only when the framing changes or when somebody takes a
    /// building, and blitted in order every frame. Per frame the map draws the things
    /// that actually move: crews, the crowd, cars, shipping, order markers, the
    /// selection box. That is the handoff's performance note, and it is the difference
    /// between a prototype with three hundred buildings and a city with thousands.
    ///
    /// THE ONE DEPARTURE FROM THE SHEET is the scale. The handoff pins it at 1 px = 8 m
    /// and never moves it; this map rides the camera's boom instead, so the wheel shows
    /// more ground rather than bigger pixels - about a metre to the pixel as the plan
    /// comes up over the street, about eleven with the whole city in frame, which puts
    /// the sheet's own figure near the far end of the wheel. Decided deliberately: the
    /// map has always been the camera in this project, and a fixed sheet would have
    /// been a worse map than the one it replaced. Everything else in the sheet's
    /// rendering section is kept exactly.
    /// </summary>
    public class DemoMap : MonoBehaviour, MapSurface.IReader
    {
        // ------------------------------------------------------------------- orders

        /// <summary>Docked beside the book: over the top bar (20) and the crew bar
        /// (22), clear of the book itself (110).</summary>
        const int DockedOrder = 30;

        /// <summary>Full screen: UNDER the HUD - the map is the ground the top bar and
        /// the crew blocks float on - but over the lot overlay (10) and the world's own
        /// crew markers (1).</summary>
        const int FullOrder = 15;

        /// <summary>The corner map, while the player is down in the street.</summary>
        const int CornerOrder = 18;

        // ------------------------------------------------------------------ framing

        /// <summary>Metres of ground the map shows down its height for every metre of
        /// boom. Chosen so the swap at 180 m is a swap of STYLE and not of place: the
        /// terminal comes up showing about what the camera had in the frame.</summary>
        const float BoomToMetres = 1.15f;

        /// <summary>How much more than the town the last click of the wheel shows.</summary>
        const float CityFrame = 1.25f;

        /// <summary>How far past the grid's own edge an outlying quarter may drag the
        /// frame: enough for a port, not enough for the island's far shore.</summary>
        const float ReachOut = 900f;

        /// <summary>Metres of ground down the corner panel - about four blocks, which is
        /// as much as a man on foot needs to see around him.</summary>
        const float CornerMetres = 300f;

        const float WorldMargin = 6f;

        // The corner panel is the raster at 1:1 in the terminal's own units, so its
        // pixels are square and exactly one unit each.
        // The postcard is sized in AUTHORED units, so it stays the same size on screen
        // whatever resolution the raster inside it is drawn at.
        const float CornerWidth = MapRaster.AW;
        const float CornerHeight = MapRaster.AH;
        const float CornerInset = 12f;

        // The docked panel's inset inside the right half.
        const float PanelLeft = 14f, PanelRight = 18f, PanelTop = 50f, PanelBottom = 18f;

        /// <summary>Under this many pixels of travel on the raster a press is a click
        /// and not a box. The sheet's own figure.</summary>
        const float DragSlack = 2f;

        /// <summary>How close a click has to land to pick a man: the sheet's tolerance,
        /// wider across than down because a figure is one pixel by three.</summary>
        const float PickX = 2.5f, PickY = 3f;

        /// <summary>How often the rail's figures are re-counted. Counting who holds what
        /// walks every building in the city, which is not a per-frame job.</summary>
        const float RailInterval = 0.5f;

        /// <summary>How long the map keeps asking the city for its buildings before it
        /// accepts that there are none. The city stands up over a few seconds; this is
        /// several times that.</summary>
        const float GatherFor = 25f;

        const float GatherEvery = 0.5f;

        /// <summary>The street's own double-click rule: the same click twice, quickly,
        /// and the crew runs the bulk of the way instead of walking it.</summary>
        const float DoubleClick = 0.45f;

        /// <summary>How near the second click has to land, in metres of ground.</summary>
        const float DoubleSlack = 12f;

        /// <summary>How often the map asks who holds what. The families are seated after
        /// the map is built and a front can burn down later, so the answer changes - but
        /// over minutes, not frames.</summary>
        const float TurfInterval = 0.75f;

        // ------------------------------------------------------------------ the city

        RoadDemoBuilder _builder;
        Transform _blockRoot;
        BuildingCardPicker _picker;
        DemoCamera _rig;
        Camera _cam;
        List<CivilianAgent> _civilians;
        List<PoliceFootPatrol> _officers;
        List<DemoVehicle> _cars;
        List<PolicePatrolCar> _policeCars;
        DemoCrews _crews;

        Rect _world;    // the grid's own extent
        Rect _reach;    // that and the quarters hanging off it

        // ----------------------------------------------------------------- the layers

        readonly MapRaster _screen = new MapRaster();
        readonly MapRaster _ground = new MapRaster();

        /// <summary>The ground with the turf tint multiplied into it. The overlay is a
        /// multiply against the BASE and nothing else, so it can be baked once into a
        /// second copy rather than re-multiplied over half a million pixels every frame;
        /// a frame with the overlay on then costs the same blit as one with it off.</summary>
        readonly MapRaster _tinted = new MapRaster();
        readonly MapBuildings _buildings = new MapBuildings();
        readonly MapTurf _turf = new MapTurf();
        readonly MapOwnership _owned = new MapOwnership();
        readonly MapAgents _agents = new MapAgents();
        readonly MapCrews _book = new MapCrews();
        readonly TacticalHud _hud = new TacticalHud();

        MapSheet _sheet;
        bool _framed;
        MapSheet _groundBaked;
        bool _groundReady;
        MapSheet _tintedFor;
        int _tintedTurf = -1;
        bool _tintedReady;

        // ------------------------------------------------------------------- the HUD

        enum Mode { Off, Corner, Docked, Full }

        Mode _mode = Mode.Off;

        /// <summary>Whether the postcard is shown while the player is down in the
        /// street. The one thing on this map a scene may want to turn off.</summary>
        public bool minimap = true;

        Canvas _canvas;
        GameObject _panel;
        RectTransform _panelRect;
        Image _panelFace;

        enum Look { None, Building, Crew, District }

        Look _look;
        int _lookAt = -1;

        readonly HashSet<int> _selected = new HashSet<int>();
        readonly List<MapOrders.Marker> _markers = new List<MapOrders.Marker>();
        readonly List<string> _log = new List<string>();
        readonly List<string> _actions = new List<string>();
        readonly List<TacticalHud.MenuRow> _menuRows = new List<TacticalHud.MenuRow>();
        readonly StringBuilder _text = new StringBuilder(384);

        bool _turfOn = true;
        float _railDue;
        float _turfDue;
        float _gatherDue;
        bool _gathered;
        Vector2 _dragFrom, _dragTo;
        bool _dragging, _dragged;
        Vector2 _hover;
        bool _hovering;
        Vector2 _menuWorld;
        MapBuilding _menuBuilding;
        DemoCrews.Unit _menuTarget;
        GangFront _menuFront;
        Vector2 _walkedTo;
        float _walkedAt = -100f;
        float _laidDistance;

        // Camera state held while the map owns the screen.
        int _camMask;
        CameraClearFlags _camClear;
        Color _camBackground;
        bool _camPost;
        System.Func<Vector2, bool> _previousVeto;

        // ------------------------------------------------------------------- wiring

        public void Init(RoadDemoBuilder builder, Transform blockRoot,
            BuildingCardPicker picker, DemoCamera rig, List<CivilianAgent> civilians,
            List<PoliceFootPatrol> officers, List<DemoVehicle> cars,
            List<PolicePatrolCar> policeCars, DemoCrews crews)
        {
            _builder = builder;
            _blockRoot = blockRoot;
            _picker = picker;
            _rig = rig;
            _cam = rig != null ? rig.GetComponent<Camera>() : null;
            _civilians = civilians;
            _officers = officers;
            _cars = cars;
            _policeCars = policeCars;
            _crews = crews;
        }

        void Start()
        {
            if (_builder == null || _builder.LotPlans.Count == 0)
            {
                // No city to draw - a map of nothing is worse than no map.
                Destroy(this);
                return;
            }

            MeasureWorld();
            Build();

            Say("SURVEY UPLOADED");
            ReadCity();

            // How far back the wheel may go: the TOWN in the frame with a hand's width
            // of country round it, not the whole island.
            if (_rig != null)
            {
                var wants = Mathf.Max(_reach.height,
                    _reach.width / ((float)MapRaster.AW / MapRaster.AH));
                _rig.mapCeiling = Mathf.Clamp(wants * CityFrame / BoomToMetres, 260f, 6000f);
            }

            // A click on the terminal is a click on the MAP and never also on the
            // building it happens to be drawn over.
            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        /// <summary>
        /// Read the city: its quarters, its districts, and every footprint standing in
        /// it.
        ///
        /// Called until it works, not once. The city is instantiated over several
        /// frames, and this component is created by the builder and starts on the frame
        /// after - which is sometimes before the blocks are in the scene and sometimes
        /// after. The old plan degraded quietly when it ran early (it drew the streets
        /// off the builder's own tables and simply had nothing clickable on them); this
        /// map is MADE of the footprints, so running early leaves a city with no
        /// buildings on it at all, and the failure is silent and total.
        ///
        /// So it re-reads on a slow timer until a footprint turns up, and gives up after
        /// <see cref="GatherFor"/> - by which point a city that still has no buildings
        /// genuinely has none.
        /// </summary>
        void ReadCity()
        {
            MapBase.Look();
            _turf.Collect(_builder);
            _buildings.Collect(_builder, _blockRoot, _turf);
            _turf.Resolve(_owned);
            _groundReady = false;

            if (_buildings.Count > 0)
            {
                _gathered = true;
                Say(_buildings.Count + " FOOTPRINTS ON FILE");
            }
        }

        bool ClaimsClick(Vector2 screen)
        {
            if (_mode != Mode.Off && _panelRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screen))
                return true;
            return _previousVeto != null && _previousVeto(screen);
        }

        void MeasureWorld()
        {
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            _world = Rect.MinMaxRect(
                vx[0] - _builder.VerticalHalfWidth(0) - WorldMargin,
                hz[0] - _builder.HorizontalHalfWidth(0) - WorldMargin,
                vx[vx.Length - 1] + _builder.VerticalHalfWidth(vx.Length - 1) + WorldMargin,
                hz[hz.Length - 1] + _builder.HorizontalHalfWidth(hz.Length - 1) + WorldMargin);

            // What the map has to FRAME is not only the grid: the quarters hang off it
            // and are as much the town as the blocks are. Capped, though - one district
            // sited half a mile out must not shrink the city to a thumbnail.
            _reach = _world;
            foreach (var district in _builder.DistrictPlans)
            {
                _reach.xMin = Mathf.Min(_reach.xMin, district.World.xMin);
                _reach.xMax = Mathf.Max(_reach.xMax, district.World.xMax);
                _reach.yMin = Mathf.Min(_reach.yMin, district.World.yMin);
                _reach.yMax = Mathf.Max(_reach.yMax, district.World.yMax);
            }
            _reach = Rect.MinMaxRect(
                Mathf.Max(_reach.xMin, _world.xMin - ReachOut),
                Mathf.Max(_reach.yMin, _world.yMin - ReachOut),
                Mathf.Min(_reach.xMax, _world.xMax + ReachOut),
                Mathf.Min(_reach.yMax, _world.yMax + ReachOut));
        }

        // -------------------------------------------------------------------- build

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = FullOrder;

            // One unit of this canvas is one pixel of the design sheet, so every figure
            // in TacticalHud is the figure the handoff specifies rather than a
            // conversion of it.
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(TacticalHud.PageWidth, TacticalHud.PageHeight);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            if (!EventSystem.current)
            {
                var host = new GameObject("EventSystem");
                host.AddComponent<EventSystem>();
                host.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var panel = new GameObject("Map Panel", typeof(RectTransform));
            _panelRect = (RectTransform)panel.transform;
            _panelRect.SetParent(transform, false);
            _panel = panel;

            _panelFace = panel.AddComponent<Image>();
            _panelFace.color = MapPalette.Page;
            _panelFace.raycastTarget = true;

            _hud.Build(_panelRect, _screen, this);
            _hud.OnToggleTurf = ToggleTurf;
            _hud.OnSelectAll = SelectAll;
            _hud.OnPickCrew = PickCrew;
            _hud.OnFocusCrew = FocusCrew;
            _hud.OnAction = RunAction;
            _hud.OnMenuItem = RunMenuItem;

            _hud.SetCity(_builder.Streets != null && !string.IsNullOrEmpty(_builder.Streets.City)
                ? _builder.Streets.City
                : "THE CITY");
            _hud.SetTurf(_turfOn);
            PaintInspector();

            ApplyMode(Mode.Off);
        }

        // --------------------------------------------------------------------- mode

        void Update()
        {
            if (_panel == null)
                return;

            var want = LivingCity.UI.PersonnelAlmanac.IsOpen ? Mode.Docked
                : _rig != null && _rig.MapOut ? Mode.Full
                : minimap && !LivingCity.UI.StrategicMapHud.IsOpen ? Mode.Corner
                : Mode.Off;

            if (want != _mode)
            {
                // Going down into the street: the player lands on the place he had the
                // pointer over, not on whatever was in the middle of the sheet.
                if (want != Mode.Full && want != Mode.Docked && _mode == Mode.Full &&
                    _hovering && _rig != null)
                {
                    var under = _sheet.ToWorld(_hover);
                    _rig.pivot = new Vector3(under.x, _rig.pivot.y, under.y);
                }
                ApplyMode(want);
            }

            if (_mode == Mode.Full || _mode == Mode.Docked || _mode == Mode.Corner)
                Bars(_mode != Mode.Full);

            if (!_gathered && Time.timeSinceLevelLoad < GatherFor &&
                Time.unscaledTime >= _gatherDue)
            {
                _gatherDue = Time.unscaledTime + GatherEvery;
                ReadCity();
            }

            if (_mode == Mode.Full || _mode == Mode.Docked)
                Keys();
        }

        /// <summary>The keys the footer strip has always advertised and the prototype
        /// never wired: turf, orders, clear.</summary>
        void Keys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f2Key.wasPressedThisFrame)
                ToggleTurf();

            if (keyboard.f3Key.wasPressedThisFrame && _hovering)
                OpenMenu(_hover);

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_hud.MenuOpen)
                {
                    _hud.HideMenu();
                }
                else if (_selected.Count > 0 || _look != Look.None)
                {
                    _selected.Clear();
                    _look = Look.None;
                    _lookAt = -1;
                    Say("SELECTION CLEARED");
                    PaintInspector();
                }
            }
        }

        void ApplyMode(Mode mode)
        {
            var was = _mode == Mode.Docked || _mode == Mode.Full;
            _mode = mode;
            var on = mode == Mode.Docked || mode == Mode.Full;

            if (on != was)
            {
                DemoAudio.Ui(on ? DemoSounds.MapOpen : DemoSounds.MapClose);
                if (_picker)
                    _picker.enabled = !on;
                _hud.HideMenu();
                _look = Look.None;
                _lookAt = -1;
                PaintInspector();
            }

            // Coming up out of the street: the terminal opens around the ground the
            // pointer was over.
            if (mode == Mode.Full && !was && _cam != null && _rig != null &&
                Mouse.current != null)
            {
                var ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                var ground = new Plane(Vector3.up, new Vector3(0f, _rig.pivot.y, 0f));
                if (ground.Raycast(ray, out var along) && along > 0f && along < 4000f)
                {
                    var hit = ray.GetPoint(along);
                    _rig.pivot = new Vector3(hit.x, _rig.pivot.y, hit.z);
                }
            }

            _panel.SetActive(mode != Mode.Off);

            // The city's own bars are not wanted on the map: the terminal prints its
            // own clock and its own roster, and a second set of both floating over the
            // picture is two too many. Their canvases are switched off rather than their
            // objects, so both keep working and come back exactly as they were when the
            // player drops into the street.
            Bars(mode != Mode.Full);

            if (mode == Mode.Corner)
            {
                _canvas.sortingOrder = CornerOrder;
                _panelRect.anchorMin = new Vector2(1f, 0f);
                _panelRect.anchorMax = new Vector2(1f, 0f);
                _panelRect.pivot = new Vector2(1f, 0f);
                _panelRect.anchoredPosition = new Vector2(-CornerInset, CornerInset);
                _panelRect.sizeDelta = new Vector2(CornerWidth, CornerHeight);
                _panelFace.enabled = true;
            }
            else if (mode == Mode.Docked)
            {
                _canvas.sortingOrder = DockedOrder;
                _panelRect.pivot = new Vector2(0.5f, 0.5f);
                _panelRect.anchorMin = new Vector2(0.5f, 0f);
                _panelRect.anchorMax = Vector2.one;
                _panelRect.offsetMin = new Vector2(PanelLeft, PanelBottom);
                _panelRect.offsetMax = new Vector2(-PanelRight, -PanelTop);
                _panelFace.enabled = true;
            }
            else if (mode == Mode.Full)
            {
                _canvas.sortingOrder = FullOrder;
                _panelRect.pivot = new Vector2(0.5f, 0.5f);
                _panelRect.anchorMin = Vector2.zero;
                _panelRect.anchorMax = Vector2.one;
                _panelRect.offsetMin = Vector2.zero;
                _panelRect.offsetMax = Vector2.zero;
                _panelFace.enabled = true;
            }

            HoldCamera(mode == Mode.Full);
        }

        /// <summary>
        /// The city's own bars, and whether they are wanted.
        ///
        /// Held as canvases and not as objects: switching a canvas off stops it drawing
        /// and leaves everything under it alive, so the clock goes on ticking and the
        /// crew blocks go on rendering their little portraits - both come back exactly
        /// as they were the moment the player drops into the street.
        ///
        /// Every canvas in the tree, not the one on the root: the crew bar keeps a
        /// canvas per crew block and none at all on itself, which is why looking for one
        /// component on one object found nothing and left the bar standing over the map.
        /// </summary>
        readonly List<(Canvas canvas, bool was)> _bars = new List<(Canvas, bool)>();
        bool _barsFound;

        void Bars(bool show)
        {
            if (!_barsFound)
            {
                var top = Object.FindAnyObjectByType<DemoTopBar>();
                var crew = CrewBar.Instance;
                if (top == null && crew == null)
                    return;   // neither is up yet - ask again next time

                _bars.Clear();
                Collect(top != null ? top.transform : null);
                Collect(crew != null ? crew.transform : null);
                _barsFound = _bars.Count > 0;
            }

            for (var i = 0; i < _bars.Count; i++)
                if (_bars[i].canvas != null)
                    _bars[i].canvas.enabled = show && _bars[i].was;
        }

        void Collect(Transform root)
        {
            if (root == null)
                return;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                _bars.Add((canvas, canvas.enabled));
        }

        /// <summary>
        /// While the terminal covers the screen the city behind it is drawn for nobody,
        /// so the camera is told to render nothing: the frame is cleared to the map's
        /// own well and the pass costs a clear. Put back exactly as it was on the way
        /// down into the street.
        /// </summary>
        void HoldCamera(bool hold)
        {
            if (_cam == null)
                return;

            if (hold)
            {
                if (_cam.cullingMask == 0)
                    return;
                _camMask = _cam.cullingMask;
                _camClear = _cam.clearFlags;
                _camBackground = _cam.backgroundColor;
                _cam.cullingMask = 0;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = MapPalette.Well;
                var data = _cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    _camPost = data.renderPostProcessing;
                    data.renderPostProcessing = false;
                }
            }
            else if (_cam.cullingMask == 0 && _camMask != 0)
            {
                _cam.cullingMask = _camMask;
                _cam.clearFlags = _camClear;
                _cam.backgroundColor = _camBackground;
                var data = _cam.GetUniversalAdditionalCameraData();
                if (data != null)
                    data.renderPostProcessing = _camPost;
            }
        }

        // ------------------------------------------------------------------ framing

        /// <summary>Where the sheet is held and at what scale: the camera's own pivot
        /// and boom while the map IS the view, a fixed bite of ground on the postcard,
        /// and the whole city when it is docked beside the book.</summary>
        MapSheet Framing()
        {
            // AUTHORED units, not real pixels. MapSheet.Metres is metres to an authored
            // unit and the sheet is still 320x200 of those however many real pixels it
            // is rasterised into - divide by the real height instead and the whole map
            // comes up three times too close, which is exactly what it did.
            if (_mode == Mode.Full && _rig != null)
                return new MapSheet(new Vector2(_rig.pivot.x, _rig.pivot.z),
                    Mathf.Max(40f, _rig.distance * BoomToMetres) / MapRaster.AH);

            if (_mode == Mode.Corner && _rig != null)
                return new MapSheet(new Vector2(_rig.pivot.x, _rig.pivot.z),
                    CornerMetres / MapRaster.AH);

            var fit = Mathf.Max(_reach.width / MapRaster.AW, _reach.height / MapRaster.AH);
            return new MapSheet(_reach.center, fit * 1.02f);
        }

        // ------------------------------------------------------------------- render

        void LateUpdate()
        {
            if (_mode == Mode.Off || _panel == null)
                return;

            // The wheel zooms about the POINTER: whatever street corner is under the
            // mouse stays under it, so zooming in walks the picture towards the block
            // the player is looking at instead of straight down the middle. Worked out
            // against the framing the last frame was drawn with.
            // _framed, not just _hovering: on the first frame the sheet is still the
            // default one and anchoring against it would throw the camera across the
            // island.
            if (_framed && _mode == Mode.Full && _rig != null && _hovering &&
                !Mathf.Approximately(_rig.distance, _laidDistance))
                AnchorZoom(_hover);
            _laidDistance = _rig != null ? _rig.distance : 0f;

            _sheet = Framing();
            _framed = true;

            var chrome = _mode != Mode.Corner;
            _hud.Layout(_panelRect.rect.size, chrome);

            Compose();
            _screen.Apply();

            if (chrome)
                Rail();
            _hud.SetLabels(Lettering());
        }

        /// <summary>
        /// The frame, in the handoff's own order: the ground, the turf over it, the
        /// buildings over that, then everything that moves.
        /// </summary>
        void Compose()
        {
            if (!_groundReady || !_groundBaked.Matches(_sheet))
            {
                MapBase.Bake(_ground, _sheet, _builder, _world);
                _groundBaked = _sheet;
                _groundReady = true;
                _tintedReady = false;
            }

            // Asked on its own timer and not the rail's, and asked whether the overlay
            // is showing or not: a building wears its district's colour even with the
            // wash switched off, so a family that loses a door has to reach the
            // buildings either way.
            if (Time.unscaledTime >= _turfDue)
            {
                _turfDue = Time.unscaledTime + TurfInterval;
                if (_turf.Resolve(_owned))
                    _buildings.Invalidate();
                _book.Collect(_crews, _turf);
            }

            if (_turfOn)
            {
                if (!_tintedReady || _tintedTurf != _turf.Version ||
                    !_tintedFor.Matches(_sheet))
                {
                    _tinted.Blit(_ground);
                    _turf.Tint(_tinted, _sheet);
                    _tintedFor = _sheet;
                    _tintedTurf = _turf.Version;
                    _tintedReady = true;
                }
                _screen.Blit(_tinted);
            }
            else
            {
                _screen.Blit(_ground);
            }

            _screen.Over(_buildings.Layer(_sheet, _turf, _owned));

            if (_look == Look.Building)
            {
                var building = _buildings.Get(_lookAt);
                if (building != null && _sheet.Sees(building.World))
                    MapAgents.Blink(_screen, _sheet.RealBox(building.World), Time.unscaledTime);
            }

            _agents.Vehicles(_screen, _sheet, _cars, _policeCars, _crews);
            _agents.Crews(_screen, _sheet, _crews, _civilians, _officers,
                _selected, _look == Look.Crew ? _lookAt : int.MinValue,
                _mode != Mode.Corner, Time.unscaledTime);
            _agents.Ships(_screen, _sheet);

            MapAgents.Markers(_screen, _sheet, _markers);

            if (_dragging && _dragged)
                MapAgents.SelectionBox(_screen, _dragFrom, _dragTo);
        }

        void AnchorZoom(Vector2 raster)
        {
            var under = _sheet.ToWorld(raster);
            // Authored throughout: the pointer arrives in authored units and the
            // anchor has to be worked out in the same space the sheet is framed in.
            var grown = Mathf.Max(40f, _rig.distance * BoomToMetres) / MapRaster.AH;
            var offset = new Vector2(raster.x - MapRaster.AW * 0.5f,
                                     MapRaster.AH * 0.5f - raster.y) * grown;
            var centre = under - offset;
            _rig.pivot = new Vector3(centre.x, _rig.pivot.y, centre.y);
        }

        // -------------------------------------------------------------- the pointer

        public void MapPress(Vector2 raster, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Right)
            {
                OpenMenu(raster);
                return;
            }

            // A left click anywhere closes an open menu first - the sheet's rule.
            _hud.HideMenu();

            if (button != PointerEventData.InputButton.Left)
                return;

            _dragging = true;
            _dragged = false;
            _dragFrom = raster;
            _dragTo = raster;
        }

        public void MapDrag(Vector2 raster, bool ended)
        {
            if (!_dragging)
                return;
            _dragTo = raster;
            if (Mathf.Abs(raster.x - _dragFrom.x) > DragSlack ||
                Mathf.Abs(raster.y - _dragFrom.y) > DragSlack)
                _dragged = true;
        }

        public void MapRelease(Vector2 raster, PointerEventData.InputButton button)
        {
            if (button != PointerEventData.InputButton.Left || !_dragging)
                return;

            _dragging = false;
            _dragTo = raster;

            if (_dragged)
            {
                BoxSelect();
                _dragged = false;
                return;
            }

            Click(raster);
        }

        public void MapHover(Vector2 raster, bool over)
        {
            _hover = raster;
            _hovering = over;
        }

        /// <summary>The wheel over the map is the camera's boom - the map IS the
        /// camera, so there is nothing here to zoom separately.</summary>
        public void MapScroll(Vector2 raster, float delta)
        {
        }

        // -------------------------------------------------------------- the picking

        /// <summary>
        /// One click, resolved in the handoff's order: a crew of the player's, then
        /// anybody else's man, then a building, then the district under it.
        /// </summary>
        void Click(Vector2 raster)
        {
            // The postcard is a minimap: bare ground on it means "take me there", which
            // is what a minimap is for. A building still opens its card.
            var building = _buildings.At(raster, _sheet);

            if (_mode == Mode.Corner && building == null)
            {
                if (_rig != null)
                {
                    var ground = _sheet.ToWorld(raster);
                    _rig.pivot = new Vector3(ground.x, _rig.pivot.y, ground.y);
                }
                return;
            }

            var unit = UnitAt(raster, playerOnly: true);
            if (unit != null)
            {
                Take(unit);
                Say(Crewname(unit) + " SELECTED");
                PaintInspector();
                _railDue = 0f;
                return;
            }

            var other = UnitAt(raster, playerOnly: false);
            if (other != null)
            {
                _look = Look.Crew;
                _lookAt = other.CrewId;
                _selected.Clear();
                PaintInspector();
                return;
            }

            if (building != null)
            {
                _look = Look.Building;
                _lookAt = building.Id;
                Say("INSPECT " + building.Name);
                PaintInspector();
                return;
            }

            var district = _turf.At(_sheet.ToWorld(raster));
            _look = district != null ? Look.District : Look.None;
            _lookAt = district != null ? district.Id : -1;
            _selected.Clear();
            PaintInspector();
        }

        /// <summary>The crew a click landed on. The selection is by CREW and not by man
        /// - the city gives orders to a crew, never to one of its hoods - so any man in
        /// the box puts his whole crew in the selection.</summary>
        DemoCrews.Unit UnitAt(Vector2 raster, bool playerOnly)
        {
            if (_crews == null)
                return null;

            foreach (var unit in _crews.Units)
            {
                if (unit == null || unit.Wiped)
                    continue;
                if (playerOnly && (unit.Faction != 0 || unit.IsPolice))
                    continue;

                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null)
                        continue;
                    var at = _sheet.ToPx(man.Tf.position);
                    if (Mathf.Abs(at.x - raster.x) < PickX && Mathf.Abs(at.y - raster.y) < PickY)
                        return unit;
                }
            }
            return null;
        }

        /// <summary>
        /// Put a crew under the player's hand - HERE and in the city both.
        ///
        /// The street already has a selected crew (<see cref="DemoCrews.Selected"/>) and
        /// every order in the game is given to it. So the map does not keep a second,
        /// private idea of who is chosen: picking a crew here picks it there, and an
        /// order given on the map is the same order, to the same crew, through the same
        /// verb, as one given in the street.
        /// </summary>
        void Take(DemoCrews.Unit unit)
        {
            _selected.Clear();
            _look = Look.None;
            _lookAt = -1;
            if (unit == null)
            {
                _crews?.Select(null);
                return;
            }
            _selected.Add(unit.CrewId);
            _look = Look.Crew;
            _lookAt = unit.CrewId;
            _crews?.Select(unit);
        }

        void BoxSelect()
        {
            var x0 = Mathf.Min(_dragFrom.x, _dragTo.x);
            var x1 = Mathf.Max(_dragFrom.x, _dragTo.x);
            var y0 = Mathf.Min(_dragFrom.y, _dragTo.y);
            var y1 = Mathf.Max(_dragFrom.y, _dragTo.y);

            // The box may sweep up several crews, but the city gives orders to ONE -
            // so the box picks the crew with the most men standing inside it, which is
            // the one the player was drawing the box around.
            DemoCrews.Unit best = null;
            var most = 0;
            if (_crews != null)
            {
                foreach (var unit in _crews.Units)
                {
                    if (unit == null || unit.Wiped || unit.Faction != 0 || unit.IsPolice)
                        continue;
                    var inside = 0;
                    foreach (var man in unit.All())
                    {
                        if (man == null || man.Dead || man.Tf == null)
                            continue;
                        var at = _sheet.ToPx(man.Tf.position);
                        if (at.x >= x0 && at.x <= x1 && at.y >= y0 && at.y <= y1)
                            inside++;
                    }
                    if (inside <= most)
                        continue;
                    most = inside;
                    best = unit;
                }
            }

            Take(best);
            Say(best != null ? Crewname(best) + " SELECTED" : "NO CREW IN BOX");
            PaintInspector();
            _railDue = 0f;   // the readouts say something different now
        }

        /// <summary>
        /// Hand a building to the player. The design sheet's CLAIM: the deed flips, a
        /// marker goes down, the layers re-bake and the log says so. What it COSTS, who
        /// may, and what the family it was taken from does about it is a rule nobody has
        /// written - it lives behind MapOrders.Claimed and nowhere in this class.
        /// </summary>
        void Claim(MapBuilding building)
        {
            if (building == null)
                return;

            var player = LivingCity.Gangs.GangCatalog.PlayerGangId;
            if (!_owned.Claim(building.Id, player))
            {
                Say("ALREADY HELD");
                return;
            }

            _buildings.Invalidate();
            _turf.Invalidate();
            Mark(building.World.center, MapOrders.Kind.Claim);
            Say("CLAIMED " + building.Name);
            MapOrders.Claimed?.Invoke(building, player);
            PaintInspector();
        }

        // ------------------------------------------------------------------ orders

        /// <summary>
        /// The right button, and it does exactly what the right button does in the
        /// street.
        ///
        /// The map used to carry an order menu of its own - MOVE HERE, ATTACK HERE,
        /// PATROL, HOLD, FALL BACK - invented from the design sheet. That was wrong for
        /// this game: the city already has a set of orders, the player already knows
        /// them, and a second vocabulary that only exists on the map is a second game.
        /// So this resolves the click the way <see cref="CrewOverlay.ReadRightClick"/>
        /// resolves it, in the same order, calling the same verbs on
        /// <see cref="DemoCrews"/>:
        ///
        ///   nothing at all unless one of the player's crews is selected;
        ///   a rival's man          -> KILL / MOTO DRIVE-BY / BOMBA;
        ///   a rival family's door  -> BOMBA;
        ///   anywhere else          -> the crew walks there (twice, quickly, and it runs).
        ///
        /// The refusals come from the crews too, so a row that cannot be taken says why
        /// in the same words the street would use.
        /// </summary>
        void OpenMenu(Vector2 raster)
        {
            _hud.HideMenu();
            _menuWorld = _sheet.ToWorld(raster);
            _menuBuilding = null;
            _menuTarget = null;
            _menuFront = null;
            _menuRows.Clear();

            var crew = _crews != null ? _crews.Selected : null;
            if (crew == null || crew.Wiped)
            {
                Say("NO CREW SELECTED");
                return;
            }

            // A rival's man under the pointer.
            var target = UnitAt(raster, playerOnly: false);
            if (target != null && target.Faction != 0 && !target.IsPolice && !target.Wiped)
            {
                _menuTarget = target;
                Rival(crew, target);
                _hud.ShowMenu(Fraction(raster), Caps(target.GangName), _menuRows);
                return;
            }

            // A rival family's premises.
            var building = _buildings.At(raster, _sheet);
            if (building?.Front != null && building.Front.GangId != 0)
            {
                _menuFront = building.Front;
                _menuBuilding = building;
                var can = _crews.CanBombThrow(crew, _menuFront.Door);
                _menuRows.Add(new TacticalHud.MenuRow
                {
                    Label = "BOMBA",
                    Note = can
                        ? "a grenade on " + _menuFront.GangName + "'s doorstep"
                        : (_crews.BombRefusal ?? "no grenades"),
                    Lit = can,
                });
                _hud.ShowMenu(Fraction(raster), Caps(building.Name), _menuRows);
                return;
            }

            // Bare ground: no card at all, the same as the street. One click walks the
            // crew there, the same click twice runs them.
            Walk(_menuWorld);
        }

        void Rival(DemoCrews.Unit crew, DemoCrews.Unit target)
        {
            _menuRows.Add(new TacticalHud.MenuRow
            {
                Label = "KILL",
                Note = "the crew goes in on him",
                Lit = true,
            });

            var bike = _crews.BikeOf(crew);
            var machines = _crews.DriveByMachines(crew, target);
            var canRide = bike != null && machines > 0;
            _menuRows.Add(new TacticalHud.MenuRow
            {
                Label = "MOTO DRIVE-BY",
                Note = !canRide
                    ? (bike == null
                        ? "no machine - buy one in the ledger"
                        : _crews.DriveByRefusal ?? "not now")
                    : machines == 1
                        ? "two men on the " + bike.DisplayName.ToLowerInvariant() + ", one pass"
                        : machines + " machines, " + machines * 2 + " men, one pass each",
                Lit = canRide,
            });

            var canBomb = _crews.CanBombThrow(crew, target.Position);
            _menuRows.Add(new TacticalHud.MenuRow
            {
                Label = "BOMBA",
                Note = canBomb
                    ? "lob a grenade at him - it kills all it stands over"
                    : (_crews.BombRefusal ?? "no grenades"),
                Lit = canBomb,
            });
        }

        /// <summary>Send the selected crew to a place on the ground. The street's own
        /// double-click-to-run rule, measured on the map instead of on the screen.</summary>
        void Walk(Vector2 world)
        {
            var ground = new Vector3(world.x, 0f, world.y);
            var quick = Time.unscaledTime - _walkedAt <= DoubleClick &&
                        (world - _walkedTo).sqrMagnitude <= DoubleSlack * DoubleSlack;
            _walkedAt = Time.unscaledTime;
            _walkedTo = world;

            if (!_crews.OrderSelected(ground, out var destination, quick))
            {
                Say("CANNOT GO THERE");
                return;
            }

            Mark(new Vector2(destination.x, destination.z), MapOrders.Kind.Move);
            Say(quick ? "CREW RUNNING" : "CREW MOVING");
        }

        /// <summary>Where a family keeps its door, if it keeps one.</summary>
        static Vector3? DoorOf(int gangId)
        {
            var fronts = GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
                if (fronts[i] != null && fronts[i].GangId == gangId)
                    return fronts[i].Door;
            return null;
        }

        Vector2 Fraction(Vector2 raster) =>
            new Vector2(raster.x / MapRaster.AW, raster.y / MapRaster.AH);

        void Mark(Vector2 world, MapOrders.Kind kind)
        {
            _markers.Add(new MapOrders.Marker
            {
                World = world,
                Kind = kind,
                Life = MapOrders.MarkerLife,
            });
        }

        /// <summary>A row of the order card was taken. Every one of these is a verb the
        /// city already owns; the map only decides which card was open.</summary>
        void RunMenuItem(int index)
        {
            _hud.HideMenu();
            if (index < 0 || index >= _menuRows.Count || _crews == null)
                return;

            var crew = _crews.Selected;
            if (crew == null)
            {
                Say("NO CREW SELECTED");
                return;
            }

            if (_menuFront != null)
            {
                if (_crews.OrderBombFront(_menuFront))
                {
                    Mark(new Vector2(_menuFront.Door.x, _menuFront.Door.z),
                        MapOrders.Kind.Attack);
                    Say("BOMBA ON " + Caps(_menuFront.GangName));
                }
                else
                {
                    Say(Caps(_crews.BombRefusal ?? "REFUSED"));
                }
                return;
            }

            var target = _menuTarget;
            if (target == null || target.Wiped)
            {
                Say("HE IS GONE");
                return;
            }

            var where = new Vector2(target.Position.x, target.Position.z);
            switch (index)
            {
                case 0:
                    if (_crews.OrderAttack(target))
                    {
                        Mark(where, MapOrders.Kind.Attack);
                        Say("KILL ORDERED ON " + Caps(target.GangName));
                    }
                    else Say("CANNOT GO IN");
                    break;

                case 1:
                    if (_crews.OrderDriveBy(target))
                    {
                        Mark(where, MapOrders.Kind.Attack);
                        Say("DRIVE-BY ON " + Caps(target.GangName));
                    }
                    else Say(Caps(_crews.DriveByRefusal ?? "REFUSED"));
                    break;

                default:
                    if (_crews.OrderBombThrow(target))
                    {
                        Mark(where, MapOrders.Kind.Attack);
                        Say("BOMBA ON " + Caps(target.GangName));
                    }
                    else Say(Caps(_crews.BombRefusal ?? "REFUSED"));
                    break;
            }
        }

        // -------------------------------------------------------------- the rail

        readonly List<TacticalHud.GangRow> _gangRows = new List<TacticalHud.GangRow>();
        readonly List<TacticalHud.CrewRow> _crewRows = new List<TacticalHud.CrewRow>();
        readonly Dictionary<int, int> _heldBy = new Dictionary<int, int>();
        readonly Dictionary<int, int> _menBy = new Dictionary<int, int>();

        void Rail()
        {
            _hud.Blip(Time.unscaledTime);

            if (Time.unscaledTime < _railDue)
                return;
            _railDue = Time.unscaledTime + RailInterval;

            Count();

            var player = LivingCity.Gangs.GangCatalog.PlayerGangId;
            _heldBy.TryGetValue(player, out var mine);
            var total = Mathf.Max(1, _buildings.Count);

            // Selected counts BOTH: a player who has boxed three crews wants to know he
            // has eleven men, not three markers.
            var chosen = 0;
            foreach (var crew in _book.All)
                if (_selected.Contains(crew.Id))
                    chosen += crew.Strength;

            _hud.SetStats(LivingCity.Gangs.GangRegistry.NameOf(player), _book.Manpower,
                _selected.Count, chosen,
                Mathf.RoundToInt(mine * 100f / total), Clock());
            // THE scale, and the only one anyone is shown: metres to a REAL pixel,
            // which is what the inspector's footprints are measured in too.
            _hud.SetCount(_buildings.Count, _sheet.RealMetres);
            _hud.SetGangs(_gangRows);
            _hud.SetRoster(_crewRows);
            _hud.SetLog(_log);

            // A crew's card is the one thing on this rail that goes stale while it is
            // open: the man it describes is walking about. Re-read on the same tick as
            // the roster, so the two cannot disagree about what he is doing.
            if (_look == Look.Crew)
                PaintInspector();
        }

        /// <summary>Who holds what and who has how many men, counted once every half
        /// second rather than every frame - it walks every building in the city.</summary>
        void Count()
        {
            _heldBy.Clear();
            _menBy.Clear();

            foreach (var building in _buildings.All)
            {
                var gang = _buildings.GangOf(building, _turf, _owned);
                if (gang < 0)
                    continue;
                _heldBy.TryGetValue(gang, out var held);
                _heldBy[gang] = held + 1;
            }

            // Every figure below is COUNTED, never assumed: the men per family are
            // summed off the crews themselves, so a family that loses four men loses
            // them on this panel in the same half-second.
            _crewRows.Clear();
            foreach (var crew in _book.All)
            {
                if (crew.Gang == -2)
                    continue;
                if (crew.Strength > 0)
                {
                    _menBy.TryGetValue(crew.Gang, out var men);
                    _menBy[crew.Gang] = men + crew.Strength;
                }
                if (crew.Gang != 0)
                    continue;

                var leader = crew.Men.Count > 0 ? crew.Men[0] : default;
                _crewRows.Add(new TacticalHud.CrewRow
                {
                    CrewId = crew.Id,
                    Name = crew.Name,
                    Rank = crew.Rank,
                    Alias = crew.Alias,
                    Men = crew.Strength,
                    Weapon = leader.Weapon,
                    Mug = crew.Mug,
                    Order = MapOrders.StateOf(crew.Unit),
                    Condition = crew.Condition,
                    Selected = _selected.Contains(crew.Id),
                });
            }

            _gangRows.Clear();
            var total = Mathf.Max(1, _buildings.Count);
            for (var id = 0; id < LivingCity.Gangs.GangCatalog.GangCount; id++)
            {
                _heldBy.TryGetValue(id, out var held);
                _menBy.TryGetValue(id, out var men);
                if (held == 0 && men == 0 && id != 0)
                    continue;   // a family with nothing in this city is not on the sheet
                _gangRows.Add(new TacticalHud.GangRow
                {
                    Colour = LivingCity.UI.GangPalette.Of(id),
                    Name = LivingCity.Gangs.GangRegistry.NameOf(id),
                    People = men,
                    Percent = Mathf.RoundToInt(held * 100f / total),
                });
            }

            // Ranked, because the rail has room for a dozen rows and the city has
            // twenty-one families: the player first - it is his sheet - then whoever
            // holds the most ground, then whoever has the most men on it. What falls off
            // the bottom is a family with nothing, which is what the player would have
            // read last anyway.
            _gangRows.Sort((a, b) =>
            {
                var mineA = a.Name == LivingCity.Gangs.GangRegistry.NameOf(0);
                var mineB = b.Name == LivingCity.Gangs.GangRegistry.NameOf(0);
                if (mineA != mineB)
                    return mineA ? -1 : 1;
                if (a.Percent != b.Percent)
                    return b.Percent.CompareTo(a.Percent);
                return b.People.CompareTo(a.People);
            });

            // And the ground nobody holds, which on this map is most of it.
            var claimed = 0;
            foreach (var pair in _heldBy)
                claimed += pair.Value;
            _gangRows.Add(new TacticalHud.GangRow
            {
                Colour = MapPalette.UnclaimedChrome,
                Name = "UNCLAIMED",
                People = _civilians?.Count ?? 0,
                Percent = Mathf.RoundToInt((total - claimed) * 100f / total),
            });
        }

        static float Condition(DemoCrews.Unit unit)
        {
            var health = 0f;
            var most = 0f;
            foreach (var man in unit.All())
            {
                if (man == null)
                    continue;
                most += Mathf.Max(1, man.MaxHealth);
                health += man.Dead ? 0 : Mathf.Max(0, man.Health);
            }
            return most > 0f ? health / most : 0f;
        }

        static string Crewname(DemoCrews.Unit unit)
        {
            if (!string.IsNullOrEmpty(unit.Name))
                return unit.Name;
            return unit.Boss != null && !string.IsNullOrEmpty(unit.Boss.DisplayName)
                ? unit.Boss.DisplayName
                : "CREW " + unit.CrewId;
        }

        DemoClock _clock;

        string Clock()
        {
            if (_clock == null)
                _clock = Object.FindAnyObjectByType<DemoClock>();
            var clock = _clock;
            if (clock == null)
                return "--:--:--";
            var seconds = Mathf.FloorToInt(clock.Hour * 3600f);
            return $"{seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }

        // --------------------------------------------------------------- the card

        void PickCrew(int crewId)
        {
            // Through Take, not by filling the map's own set: a row in the roster picks
            // the crew for the STREET as well, exactly as clicking one of his men on the
            // sheet does. Filling only the local set was the older way and it left the
            // player with a name lit on this panel and no crew selected in the game -
            // his next right click then did nothing and said NO CREW SELECTED at him.
            Take(UnitOf(crewId));
            PaintInspector();
            _railDue = 0f;
        }

        /// <summary>
        /// Put the map over a man. The right button on a name in the roster: not an
        /// order, just "where is he" - the crew list is the only place on this terminal
        /// that names a lieutenant who may be right off the edge of the sheet, and
        /// hunting for him by dragging is no way to find him.
        ///
        /// The camera's pivot IS the map's centre, so moving it moves the map - and it
        /// leaves the player standing over that ground when he drops back into the
        /// street, which is the whole point of the map being the camera.
        /// </summary>
        void FocusCrew(int crewId)
        {
            var unit = UnitOf(crewId);
            var boss = unit != null ? unit.Boss : null;
            if (boss == null || boss.Tf == null || boss.Dead || _rig == null)
            {
                Say("NOBODY TO LOOK AT");
                return;
            }

            var at = boss.Tf.position;
            _rig.pivot = new Vector3(at.x, _rig.pivot.y, at.z);
            _look = Look.Crew;
            _lookAt = crewId;
            Say("CENTRED ON " + Caps(Crewname(unit)));
            PaintInspector();
        }

        void SelectAll()
        {
            _selected.Clear();
            if (_crews != null)
                foreach (var unit in _crews.Units)
                    if (unit != null && !unit.Wiped && unit.Faction == 0 && !unit.IsPolice)
                        _selected.Add(unit.CrewId);
            Say(_selected.Count + " CREWS SELECTED");
            _look = Look.None;
            _lookAt = -1;
            PaintInspector();
            _railDue = 0f;
        }

        void ToggleTurf()
        {
            _turfOn = !_turfOn;
            _hud.SetTurf(_turfOn);
            Say(_turfOn ? "TURF OVERLAY ON" : "TURF OVERLAY OFF");
        }

        /// <summary>
        /// The inspector, and the actions under it. Every figure printed here is one the
        /// city actually holds: the footprint is what the renderers measure, the takings
        /// come off a front's own books. What the project has no figure for - how many
        /// people live in an ordinary building, what an ordinary shop earns - is left
        /// off the card rather than made up.
        /// </summary>
        void PaintInspector()
        {
            _text.Clear();
            _actions.Clear();
            var head = "NOTHING SELECTED";

            switch (_look)
            {
                case Look.Building:
                {
                    var building = _buildings.Get(_lookAt);
                    if (building == null)
                        break;

                    var gang = _buildings.GangOf(building, _turf, _owned);
                    head = building.Name;
                    _text.Append("TYPE: ").Append(MapBuildings.Label(building.Kind)).Append('\n');
                    _text.Append("OWNER: ").Append(gang < 0
                        ? "UNCLAIMED"
                        : Caps(LivingCity.Gangs.GangRegistry.NameOf(gang))).Append('\n');
                    _text.Append("DISTRICT: ").Append(building.District.ToUpperInvariant()).Append('\n');
                    // Real pixels AND metres, on the one scale the footer prints.
                    var shot = _sheet.RealBox(building.World);
                    _text.Append("FOOTPRINT: ")
                        .Append(shot.width).Append(" X ").Append(shot.height)
                        .Append(" PX  (").Append(Mathf.RoundToInt(building.World.width))
                        .Append(" X ").Append(Mathf.RoundToInt(building.World.height))
                        .Append(" M)").Append('\n');
                    _text.Append("FLOORS: ~").Append(building.Floors)
                        .Append("  (").Append(Mathf.RoundToInt(building.Height)).Append(" M)\n");
                    if (building.Staff >= 0)
                        _text.Append("STAFF: ").Append(building.Staff).Append('\n');
                    if (building.Takings >= 0)
                        _text.Append("WEEKLY TAKE: $").Append(building.Takings).Append('\n');
                    if (building.Front != null)
                        _text.Append("FAMILY FRONT\n");

                    _actions.Add(gang == LivingCity.Gangs.GangCatalog.PlayerGangId
                        ? "ALREADY YOURS" : "CLAIM BUILDING");
                    _actions.Add("EXTORT");
                    _actions.Add("STAKEOUT");
                    _actions.Add("SET HQ");
                    break;
                }

                case Look.Crew:
                {
                    var crew = _book.Get(_lookAt);
                    if (crew == null)
                        break;
                    _actions.Clear();
                    if (crew.Gang == 0)
                        _actions.Add("TO THE DOOR");
                    else
                        _actions.Add("MARK TARGET");

                    // The crew card is not a paragraph: it is a face, a column of
                    // figures and the men on the book, so it has its own builder.
                    _hud.SetCrewCard(crew,
                        crew.Gang == -2 ? "CITY POLICE"
                            : Caps(LivingCity.Gangs.GangRegistry.NameOf(crew.Gang)),
                        MapOrders.StateOf(crew.Unit), _actions);
                    return;
                }

                case Look.District:
                {
                    var district = _turf.Get(_lookAt);
                    if (district == null)
                        break;
                    head = district.Name;
                    _text.Append("CONTROL: ").Append(district.Contested ? "CONTESTED"
                        : district.Gang < 0 ? "UNCLAIMED"
                        : Caps(LivingCity.Gangs.GangRegistry.NameOf(district.Gang)))
                        .Append('\n');
                    if (district.Kind.HasValue)
                        _text.Append("QUARTER: ").Append(Caps(district.Kind.Value.ToString()))
                            .Append('\n');
                    _text.Append("FAMILY DOORS: ").Append(district.Fronts).Append('\n');
                    _text.Append("AREA: ")
                        .Append((district.World.width / 1000f).ToString("0.0")).Append(" X ")
                        .Append((district.World.height / 1000f).ToString("0.0")).Append(" KM\n");
                    _actions.Add("PUSH TURF");
                    break;
                }
            }

            if (_text.Length == 0)
                _text.Append("Click a building, a crew, or drag a box over your men.");

            _hud.SetInspector(Caps(head), _text.ToString(), _actions);
        }

        /// <summary>Every word this map prints goes through here. A name the city never
        /// filled in - an unnamed quarter, a family with no entry in the registry - must
        /// print as a dash, not take the card down with it.</summary>
        static string Caps(string words) =>
            string.IsNullOrEmpty(words) ? "-" : words.ToUpperInvariant();

        DemoCrews.Unit UnitOf(int crewId)
        {
            if (_crews == null)
                return null;
            foreach (var unit in _crews.Units)
                if (unit != null && unit.CrewId == crewId)
                    return unit;
            return null;
        }

        /// <summary>
        /// The inspector's own buttons. CLAIM is real - it flips the deed, fires a
        /// marker and repaints. The rest are the design sheet's log-only stubs, and they
        /// stay stubs here on purpose: what extortion earns, what a stakeout watches for
        /// and what a headquarters IS are rules nobody has written.
        /// </summary>
        void RunAction(int index)
        {
            if (index < 0 || index >= _actions.Count)
                return;
            var label = _actions[index];

            switch (_look)
            {
                case Look.Building:
                {
                    var building = _buildings.Get(_lookAt);
                    if (building == null)
                        return;
                    switch (label)
                    {
                        case "CLAIM BUILDING":
                            Claim(building);
                            break;
                        case "ALREADY YOURS":
                            Say("ALREADY HELD");
                            break;
                        case "EXTORT":
                            Say("EXTORTION SET AT " + building.Name);
                            MapOrders.Extort?.Invoke(building);
                            break;
                        case "STAKEOUT":
                        {
                            var watcher = _crews != null ? _crews.Selected : null;
                            Say(watcher != null
                                ? "STAKEOUT ON " + building.Name
                                : "NO CREW SELECTED");
                            MapOrders.Stakeout?.Invoke(watcher, building);
                            break;
                        }
                        case "SET HQ":
                            Say(building.Name + " MARKED AS HQ");
                            MapOrders.MakeHq?.Invoke(building);
                            break;
                    }
                    break;
                }

                case Look.Crew:
                {
                    var unit = UnitOf(_lookAt);
                    if (unit == null)
                        return;
                    if (label == "MARK TARGET")
                    {
                        Say("TARGET MARKED: " +
                            Caps(LivingCity.Gangs.GangRegistry.NameOf(unit.Faction)));
                        return;
                    }
                    // The only order on this card is the one the street cannot give
                    // with a click, because the door may be off the screen: send them
                    // home. Everything else a crew can be told is on the right button.
                    Take(unit);
                    var door = DoorOf(unit.Faction);
                    if (door.HasValue)
                        Walk(new Vector2(door.Value.x, door.Value.z));
                    else
                        Say("NO DOOR TO GO BACK TO");
                    PaintInspector();
                    break;
                }

                case Look.District:
                {
                    var district = _turf.Get(_lookAt);
                    if (district != null)
                        Say("PUSH ORDERED INTO " + district.Name);
                    break;
                }
            }
        }

        // ------------------------------------------------------------- the lettering

        readonly List<TacticalHud.MapLabel> _lettering = new List<TacticalHud.MapLabel>();

        /// <summary>
        /// The names over the map, and the sheet's own swap: with the turf overlay ON
        /// every district wears a chip in its family's colour; with it off they are
        /// plain place names in bone white. The chips and the names are the same list -
        /// only the dress changes.
        /// </summary>
        List<TacticalHud.MapLabel> Lettering()
        {
            _lettering.Clear();
            if (_mode == Mode.Corner)
                return _lettering;

            foreach (var district in _turf.All)
            {
                if (!_sheet.Sees(district.World))
                    continue;
                var name = Caps(district.Name);
                var box = _sheet.Box(district.World);
                if (box.width < 24 || box.height < 14)
                    continue;   // no room to print a name in

                var middle = new Vector2(
                    (box.xMin + box.width * 0.5f) / MapRaster.AW,
                    (box.yMin + box.height * 0.5f) / MapRaster.AH);
                if (middle.x < 0.02f || middle.x > 0.98f || middle.y < 0.02f || middle.y > 0.98f)
                    continue;

                if (!_turfOn)
                {
                    _lettering.Add(new TacticalHud.MapLabel
                    {
                        Fraction = middle,
                        Text = name,
                        Colour = MapPalette.Contested,
                        Border = Color.clear,
                    });
                    continue;
                }

                var contested = district.Contested;
                var colour = contested
                    ? (Color)MapPalette.Contested
                    : district.Gang < 0
                        ? MapPalette.UnclaimedChrome
                        : (Color)MapPalette.Tag(district.Gang);
                var family = contested ? "CONTESTED"
                    : district.Gang < 0 ? "UNCLAIMED"
                    : Caps(LivingCity.Gangs.GangRegistry.NameOf(district.Gang));

                _lettering.Add(new TacticalHud.MapLabel
                {
                    Fraction = middle,
                    Text = family + " - " + name,
                    Colour = colour,
                    Border = contested
                        ? (Color)MapPalette.Contested
                        : district.Gang < 0
                            ? MapPalette.UnclaimedChrome
                            : LivingCity.UI.GangPalette.Of(district.Gang),
                });
            }

            return _lettering;
        }

        // ------------------------------------------------------------------ the log

        void Say(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;
            _log.Insert(0, line.ToUpperInvariant());
            while (_log.Count > 4)
                _log.RemoveAt(_log.Count - 1);
            _hud.SetLog(_log);
        }

        // --------------------------------------------------------------------- scrap

        void OnDestroy()
        {
            _screen.Release();
            _hud.Release();

            // Play-stop with the map up must give the world its picker and its camera
            // back - both live on objects that outlive this one in the editor.
            if (_mode != Mode.Off && _picker)
                _picker.enabled = true;
            Bars(true);
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
            HoldCamera(false);
        }
    }
}
