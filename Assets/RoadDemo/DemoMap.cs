using System.Collections.Generic;
using LivingCity.CameraRig;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The city as a PRINTED PLAN - the war room's map, in the manner of the 1998
    /// original: pale streets lettered along their length, blocks of little roofs,
    /// the river and the park in flat colour, fields around the town, and everybody
    /// who moves a dot over the top of it.
    ///
    /// It comes up three ways, and it is the same map every time:
    ///
    ///   CORNER - down in the street, a postcard in the bottom right corner holding
    ///            three hundred metres of ground under the camera's own pivot: the
    ///            blocks around the player, the station and the family doors on it, his
    ///            crews and the police. Clicking it takes the camera there, dragging it
    ///            pans, and the crowd is left off it - ten thousand civilians placed
    ///            every frame for a panel the size of a hand is the one thing on this
    ///            map that would cost real time.
    ///
    ///   FULL   - the player pulls the wheel back past <see cref="DemoCamera.mapAt"/>
    ///            (180 m) and the plan takes the screen. It is not a separate view:
    ///            the map's centre IS the camera's pivot and its zoom IS the boom, so
    ///            panning the map pans the camera, and pushing the wheel in past the
    ///            same line drops the player back into the street he was looking at.
    ///            It sits UNDER the HUD (sorting 15), the way a strategy map should.
    ///   DOCKED - the ledger takes the left half of the screen, and the map moves into
    ///            the right for as long as the book stands open, fitted to the whole
    ///            city and not panned.
    ///
    /// The map DRAWS the plan rather than photographing it - no second camera, no
    /// render texture. The block slabs come off the same grid arithmetic the kit is
    /// laid on (RoadDemoBuilder.LotPlans and the two half-widths), the buildings off
    /// the very colliders BuildingCardPicker raycasts in the world, so what is
    /// clickable on the map is exactly what is clickable in the scene - and the card
    /// it opens prints the same two lines the world card does. The countryside is
    /// sampled off the island's own heightfield (RoadDemoBuilder.LandHeight) into one
    /// mesh of coloured patches, so a coastline costs one draw call and no objects.
    ///
    /// Zoom and pan cost nothing per rect: every piece of the plan is laid out ONCE
    /// in metres inside a content rect, and the view is a scale and an offset on that
    /// rect. Only the moving dots are placed per frame.
    /// </summary>
    public class DemoMap : MonoBehaviour
    {
        /// <summary>Docked beside the book: over the top bar (20) and the crew bar
        /// (22), clear of the book itself (110).</summary>
        const int DockedOrder = 30;

        /// <summary>Full screen: UNDER the HUD - the map is the ground the top bar and
        /// the crew blocks float on - but over the lot overlay (10) and the world's
        /// own crew markers (1), which are drawn for the camera and mean nothing while
        /// the plan is up.</summary>
        const int FullOrder = 15;

        /// <summary>The corner map, while the player is down in the street: over the
        /// world's own overlays, under the top bar and the crew blocks.</summary>
        const int CornerOrder = 18;

        // The corner panel: bottom right, clear of the zoom readout the camera prints
        // bottom LEFT, and of the crew bar along the top.
        const float CornerWidth = 340f, CornerHeight = 250f, CornerInset = 16f;

        /// <summary>Metres of ground down the corner panel - about four blocks, which is
        /// as much as a man on foot needs to see around him and little enough that a dot
        /// on it means a street and not a district.</summary>
        const float CornerMetres = 300f;

        /// <summary>The plaques are a little smaller in the corner: the same marks, a
        /// quarter of the room to print them in.</summary>
        const float CornerMarks = 0.72f;

        // The docked panel's inset inside the right half. The top clears the demo's
        // top bar and the crew blocks under it.
        const float PanelLeft = 14f, PanelRight = 22f, PanelTop = 66f, PanelBottom = 22f;
        const float HeaderHeight = 40f;
        const float ViewPad = 12f;
        const float WorldMargin = 6f;  // metres of air around the city, so nothing touches the frame

        // ------------------------------------------------------------------- zoom

        /// <summary>Metres of ground the map shows down its height for every metre of
        /// boom. Chosen so the swap at 180 m is a swap of STYLE and not of place: the
        /// plan comes up showing about what the camera had in the frame.</summary>
        const float BoomToMetres = 1.15f;

        /// <summary>How much more than the town the last click of the wheel shows: a
        /// quarter again, so the coast and the fields frame the city instead of
        /// swallowing it.</summary>
        const float CityFrame = 1.25f;

        /// <summary>How far past the grid's own edge, in metres, an outlying quarter may
        /// drag the frame. It has to admit a port - which hangs a basin several hundred
        /// metres off the shore - and refuse the island, which is kilometres of sea. It
        /// was a FRACTION of the grid's size, which is how the plan came to leave the
        /// port off the last click of the wheel: a grid twice as wide as it is deep
        /// allowed less than half as much room north and south as east and west, and the
        /// port hangs off a shore.</summary>
        const float ReachOut = 900f;

        // ----------------------------------------------------------------- glyphs

        const float PedDot = 5f;
        const float PoliceDot = 7f;
        const float CrewDot = 8f;
        const float BossDot = 11f;
        const float CarLength = 9f, CarWidth = 5f;

        /// <summary>No footprint is drawn thinner than this, or a shed is nothing at
        /// all. Metres, because the plan is laid out in metres.</summary>
        const float MinBuilding = 3f;

        // ------------------------------------------------------------- the houses

        /// <summary>What counts as a house when the plan is drawn off the city itself:
        /// a thing under the block roots that STANDS UP and has a footprint worth a
        /// roof of its own. The catalog's footprint boxes are one to a BAKE, and a bake
        /// is a whole block interior seventy metres across - a plan drawn off those
        /// alone is a city of grey slabs. The renderers inside them are the houses, and
        /// a block drawn as a row of small roofs is most of what makes the original's
        /// map read as a town.</summary>
        const float RoofRise = 2.4f, RoofSide = 2.2f, RoofArea = 9f;

        /// <summary>The dark line round every roof, in metres of ground - so a terrace
        /// still reads as separate houses at any zoom.</summary>
        const float RoofEdge = 0.5f;

        /// <summary>A footprint this far inside one already drawn is a PART of it - a
        /// window, a door, a balcony, an air unit on the roof - and gets no roof of its
        /// own.</summary>
        const float RoofSwallow = 0.7f;

        /// <summary>The bucket the swallow test walks, metres.</summary>
        const float RoofCell = 24f;

        /// <summary>Quads to one mesh (a UI mesh may not pass 65k verts, and each quad
        /// is four), and the most roofs the plan will draw at all.</summary>
        const int RoofsPerMesh = 6000;
        const int RoofBudget = 26000;

        /// <summary>How much ground goes into one mesh. The plan is drawn once and then
        /// only scaled, but a mesh is still SUBMITTED every frame it is on, and the
        /// corner panel is up the whole time the player is in the street - so the town
        /// is cut into cells and a cell nobody is looking at is switched off.</summary>
        const float SheetCell = 340f, SheetPad = 40f;

        /// <summary>The countryside, cut the same way: bands up the map.</summary>
        const float LandBand = 500f;
        const int LandPerMesh = 3500;

        const float PopupWidth = 250f, PopupHeight = 88f, PopupLift = 10f;

        // ------------------------------------------------------------ the letters

        /// <summary>Letter heights in METRES of ground - the lettering is part of the
        /// plan and grows with it, the way it does on the original's map.</summary>
        const float StreetType = 11f, AvenueType = 15f, DistrictType = 46f, CityType = 105f;
        /// <summary>The city's own quarters: bigger than a street name, smaller than a
        /// place out of town, and gone again once the plan is close enough for the
        /// streets to carry their own names.</summary>
        const float QuarterType = 30f, QuarterMaxPx = 130f;

        /// <summary>How far apart a street name is repeated down its own street. A
        /// shade under what the closest map zoom has in the frame, so a street the
        /// player has panned to is named without him having to hunt for the corner.</summary>
        const float LabelStep = 220f;

        /// <summary>A name is printed only while it reads: no smaller than this on
        /// screen, and no larger - the city's own name goes off again once the map is
        /// close enough that the streets carry their names.</summary>
        const float MinTypePx = 8f, MaxTypePx = 210f;

        /// <summary>The size a street's name is printed at ON SCREEN, and how far its
        /// letters may be pushed from the size they were drawn at to hold it. The
        /// ceiling is generous on purpose: pulled right back the plan letters its
        /// streets rather than going silent, which is the whole difference between a
        /// map and a grey diagram.</summary>
        const float StreetTypePx = 16f, AvenueTypePx = 21f;
        const float TypeGrowLow = 0.6f, TypeGrowHigh = 4.5f;

        /// <summary>Pixels of screen between two printings of the same street name, and
        /// the width of ground a street must have to itself before it is named at all.
        /// Without the second, a city pulled back to the frame prints fourteen names
        /// across a hundred pixels and reads as a smear of ink.</summary>
        const float NameGapPx = 330f, NameLinePx = 74f;

        // ------------------------------------------------------------------ paper

        // The plan's colours: paper, not the navy the rest of the demo's screens are
        // dressed in. The map is the one screen that is a PICTURE of the city, and the
        // original's is a bright printed sheet - grey streets, pale blocks, flat green
        // country, a blue river.
        static readonly Color Sea = new Color(0.176f, 0.353f, 0.541f, 1f);
        static readonly Color Shore = new Color(0.847f, 0.788f, 0.596f, 1f);
        static readonly Color Field = new Color(0.298f, 0.549f, 0.259f, 1f);
        static readonly Color FieldAlt = new Color(0.243f, 0.478f, 0.216f, 1f);
        static readonly Color Asphalt = new Color(0.322f, 0.318f, 0.310f, 1f);
        static readonly Color Avenue = new Color(0.353f, 0.349f, 0.341f, 1f);
        static readonly Color Median = new Color(0.925f, 0.882f, 0.643f, 1f);
        static readonly Color Slab = new Color(0.812f, 0.769f, 0.690f, 1f);    // the sidewalk ring
        static readonly Color LotFace = new Color(0.686f, 0.671f, 0.627f, 1f); // the yard inside it
        static readonly Color River = new Color(0.294f, 0.404f, 0.741f, 1f);
        static readonly Color Lawn = new Color(0.373f, 0.639f, 0.286f, 1f);
        static readonly Color Deck = new Color(0.388f, 0.384f, 0.376f, 1f);

        // The fine print: the pavements the crowd walks, the kerb under them, the
        // zebra it crosses at, the paint down the middle of an ordinary street and the
        // furniture standing at the kerb. All of it drawn from the SAME data the world
        // was laid from, so what is on the plan is what is in the street.
        static readonly Color Walk = new Color(0.835f, 0.792f, 0.714f, 1f);   // the pavement
        static readonly Color Kerb = new Color(0.639f, 0.600f, 0.541f, 1f);   // its edge
        static readonly Color ZebraPaint = new Color(0.941f, 0.933f, 0.910f, 0.95f);
        static readonly Color LanePaint = new Color(0.878f, 0.871f, 0.847f, 0.80f);
        static readonly Color PropInk = new Color(0.400f, 0.384f, 0.353f, 1f);

        // The roofs. Rolled per building off where it stands, so a block reads as a
        // row of different houses and not one stamp repeated.
        static readonly Color[] Roofs =
        {
            new Color(0.812f, 0.765f, 0.651f, 1f),
            new Color(0.729f, 0.678f, 0.573f, 1f),
            new Color(0.663f, 0.545f, 0.435f, 1f),
            new Color(0.639f, 0.392f, 0.306f, 1f),
            new Color(0.749f, 0.729f, 0.706f, 1f),
            new Color(0.573f, 0.596f, 0.612f, 1f),
        };

        /// <summary>The wash over a quarter - the original tints its districts, and
        /// that tint is much of what makes a plan a MAP of somewhere.</summary>
        static readonly Color HarborWash = new Color(0.361f, 0.475f, 0.573f, 0.30f);
        static readonly Color SuburbWash = new Color(0.373f, 0.694f, 0.325f, 0.30f);
        static readonly Color PadWash = new Color(0.827f, 0.784f, 0.376f, 0.30f);

        /// <summary>The line drawn under every roof, and the green a tree wears - the
        /// plan's two smallest colours, and between them what turns a block from a slab
        /// into a row of houses with yards between them.</summary>
        static readonly Color RoofLine = new Color(0.352f, 0.325f, 0.290f, 1f);
        static readonly Color Canopy = new Color(0.271f, 0.494f, 0.243f, 1f);

        /// <summary>A footprint whose houses are drawn is not painted over them: it
        /// stays as a clear pane that takes the click, and only lights up under the
        /// pointer or under the card.</summary>
        static readonly Color Ghost = new Color(1f, 1f, 1f, 0f);
        static readonly Color HoverWash = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color PickWash = new Color(0.95f, 0.30f, 0.15f, 0.55f);

        static readonly Color Ink = new Color(0.086f, 0.078f, 0.071f, 1f);
        static readonly Color Halo = new Color(1f, 0.98f, 0.92f, 0.55f);
        static readonly Color Picked = new Color(0.95f, 0.30f, 0.15f, 1f);

        static readonly Color PoliceBlue = new Color(0.10f, 0.32f, 0.85f, 1f);
        static readonly Color PoliceRest = new Color(0.10f, 0.32f, 0.85f, 0.35f);
        static readonly Color CivilianInk = new Color(0.12f, 0.12f, 0.13f, 0.85f);
        static readonly Color TrafficInk = new Color(0.20f, 0.22f, 0.26f, 0.95f);
        static readonly Color OutfitGold = new Color(0.94f, 0.72f, 0.13f, 1f);
        static readonly Color RivalRed = new Color(0.86f, 0.17f, 0.13f, 1f);

        /// <summary>A family's front premises: a plaque the size of a trade's, in that
        /// family's own colour with its initial on it - premises never read as a man
        /// (the crews are round dots), and one family's door is never mistaken for
        /// another's. Held at its size on screen, like every other mark on the plan.</summary>
        const float FrontPx = 20f;

        /// <summary>A mob's men on the map in that family's colour (GangPalette), which
        /// is the colour its ground is washed in - twenty crews all in one red told the
        /// player only "not yours". The incident dot keeps the plain red: an incident
        /// belongs to nobody.</summary>
        static Color RivalInk(int faction) =>
            faction > 0 && faction < LivingCity.UI.GangPalette.Count
                ? LivingCity.UI.GangPalette.Of(faction)
                : RivalRed;

        // ----------------------------------------------------------------- wiring

        RoadDemoBuilder _builder;
        Transform _blockRoot;
        BuildingCardPicker _picker;
        DemoCamera _rig;
        Camera _cam;
        List<CivilianAgent> _civilians;
        List<PoliceFootPatrol> _officers;
        List<DemoVehicle> _cars;
        List<PolicePatrolCar> _policeCars;
        DemoCrews _crews;              // dealt after build; its men are plotted live
        RectTransform _moverRoot;
        readonly Dictionary<CrewWalker, Image> _crewDots = new Dictionary<CrewWalker, Image>();

        /// <summary>A footprint on the map: the same transform the world picker would
        /// hand its card, measured the same way.</summary>
        sealed class Building
        {
            public Transform Tf;
            public Rect World;      // XZ footprint
            public float Height;
            public Image Face;
            public Color Roof;
            public string Title;
            public string Body;

            /// <summary>Its own houses are drawn inside it, so the footprint itself is
            /// not painted - it is the clear pane that takes the click.</summary>
            public bool Covered;
        }

        /// <summary>Anything that moves: one Image, positioned from a transform.</summary>
        struct Mover
        {
            public Transform Tf;
            public Image Img;
            public IPatrolMarker Patrol;  // null for civilians - only police rest
            public bool Vehicle;
            public Color Tint;

            /// <summary>One of the crowd: plotted on the big plan, left off the corner
            /// panel, where ten thousand of them would cost more than the map is
            /// worth.</summary>
            public bool Civilian;

            /// <summary>The mark's smallest size in screen pixels - what it is drawn at
            /// while the plan is too far back for the thing itself to have a size worth
            /// printing. Walked into, the mark grows to the ground the man or the car
            /// really covers.</summary>
            public Vector2 Floor;
        }

        /// <summary>What the marks are ON THE GROUND, in metres: a man's shoulders, a
        /// car's body. Held at these once the plan is close enough that they come out
        /// bigger than their floor in pixels, so a street of parked cars reads as a
        /// street of parked cars and not as a row of identical pips.</summary>
        const float ManWide = 0.75f, CarBodyWide = 1.95f, CarBodyLong = 4.6f;

        /// <summary>A name printed on the plan: how tall it is in metres of ground,
        /// and the window of screen sizes it is worth reading at.</summary>
        sealed class Label
        {
            public RectTransform Rect;
            public float Metres;
            public float MinPx, MaxPx;
            public bool On = true;

            /// <summary>A street name is printed at a size ON SCREEN and not on the
            /// ground: the wheel changes what the map shows, never how big its
            /// lettering is, so a street is named at every zoom the way the original's
            /// is. Zero means the old rule - letters that grow with the ground and go
            /// off when they are too small or too large to read.</summary>
            public float ScreenPx;

            /// <summary>Which repeat down its own street this is, and how far it is to
            /// the next street either side. Between them they thin the lettering out as
            /// the plan is pulled back: every second name, then every third, and the
            /// small streets stop being named at all once they are a few pixels
            /// apart.</summary>
            public int Ordinal;
            public float GapMetres;
        }

        readonly List<Building> _buildings = new List<Building>();
        readonly List<Mover> _movers = new List<Mover>();
        readonly List<Label> _labels = new List<Label>();

        GameObject _panel;
        RectTransform _panelRect;
        Image _panelFace;
        GameObject _header;
        RectTransform _view;
        RectTransform _content;    // the plan itself, laid out in metres
        RectTransform _names;      // the lettering, which the corner panel has no room for
        Canvas _canvas;
        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupBody;
        TMP_Text _caption;

        Rect _world;          // the grid's own extent, metres - the town's ground
        Rect _reach;          // that and the villages hanging off it: what a fit frames
        Rect _paper;          // the sheet every mesh is drawn on, about the map's origin
        Vector2 _origin;      // the metre the content rect is measured from
        Vector2 _centre;      // the ground under the middle of the view
        float _scale;         // reference pixels per metre
        Vector2 _viewSize;    // the view rect the current fit was computed for
        float _laidDistance;  // the boom that fit was drawn for - what the wheel is measured against
        int _selected = -1;

        /// <summary>Off is only ever the state the map is built in: down in the street
        /// it is the corner panel, pulled back it is the screen, and beside the open
        /// book it is the book's other half.</summary>
        enum Mode { Off, Corner, Docked, Full }
        Mode _mode = Mode.Off;

        /// <summary>The corner map at all. A scene that wants the street bare turns it
        /// off; the demo keeps it.</summary>
        public bool minimap = true;

        // what the camera was doing before the plan covered it up
        int _camMask;
        CameraClearFlags _camClear;
        Color _camBackground;
        bool _camPost;

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
            CollectBuildings();
            CollectRoofs();
            Build();

            // How far back the wheel may go: the last click is the TOWN in the frame,
            // with a hand's width of country round it - not the whole island. An island
            // is three kilometres of coast and sea around a city a mile across, and a
            // plan drawn to the island puts the streets in a stamp in the middle of a
            // green field: no name is readable on it and no house is a pixel wide. The
            // camera does not know how big its city is; the map does.
            if (_rig != null)
            {
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                float wants = Mathf.Max(_reach.height, _reach.width / Mathf.Max(0.6f, aspect));
                _rig.mapCeiling = Mathf.Clamp(wants * CityFrame / BoomToMetres, 260f, 6000f);
            }

            // A click on the corner panel is a click on the MAP and never also a click
            // on the building it happens to be drawn over: the world's picker raycasts
            // the scene itself and knows nothing about the canvas in front of it.
            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        System.Func<Vector2, bool> _previousVeto;

        bool ClaimsClick(Vector2 screen)
        {
            if (_mode != Mode.Off && _panelRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screen))
                return true;
            return _previousVeto != null && _previousVeto(screen);
        }

        // -------------------------------------------------------------- the plan

        void MeasureWorld()
        {
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            _world = Rect.MinMaxRect(
                vx[0] - _builder.VerticalHalfWidth(0) - WorldMargin,
                hz[0] - _builder.HorizontalHalfWidth(0) - WorldMargin,
                vx[vx.Length - 1] + _builder.VerticalHalfWidth(vx.Length - 1) + WorldMargin,
                hz[hz.Length - 1] + _builder.HorizontalHalfWidth(hz.Length - 1) + WorldMargin);
            _origin = _world.center;

            // What the plan has to FRAME is not only the grid: the villages hang off it
            // and are as much the town as the blocks are. Capped, though - one district
            // sited half a mile out must not shrink the city itself to a thumbnail.
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

            // The sheet of paper everything is drawn on, measured from the map's origin:
            // the island if there is one, and the town and its quarters either way, with
            // room to spare. Only the meshes' own rectangles are cut from it.
            var island = _builder.IslandArea;
            float wide = Mathf.Max(island.width, _reach.width) + 2000f;
            float deep = Mathf.Max(island.height, _reach.height) + 2000f;
            _paper = new Rect(_origin.x - wide * 0.5f, _origin.y - deep * 0.5f, wide, deep);
            _centre = _origin;
        }

        /// <summary>
        /// Every clickable building, taken off the colliders under the Blocks root -
        /// which is precisely BuildingCardPicker's pick set (its pickRoot is that same
        /// root, and footprint boxes sit on bake roots and nowhere else). Measured
        /// through the renderers exactly as the world card measures them, so the two
        /// cards can never disagree about a footprint.
        /// </summary>
        void CollectBuildings()
        {
            foreach (var collider in _blockRoot.GetComponentsInChildren<Collider>(true))
            {
                var tf = collider.transform;
                var renderers = tf.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    continue;

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                _buildings.Add(new Building
                {
                    Tf = tf,
                    World = Rect.MinMaxRect(bounds.min.x, bounds.min.z,
                        bounds.max.x, bounds.max.z),
                    Height = bounds.size.y,
                    Roof = RoofFor(bounds),
                    Title = tf.name,
                    Body = $"footprint  {bounds.size.x:F0} x {bounds.size.z:F0} m\n" +
                           $"height  {bounds.size.y:F0} m",
                });
            }

            // And the quarters' own buildings: the port's sheds, the villages' houses,
            // the field's hangars. They are not under the Blocks root and carry no
            // footprint collider, so the world's picker cannot see them - but the
            // quarter reported every one of them by name as it built it, and on the plan
            // a port whose sheds cannot be clicked is scenery.
            foreach (var (area, rise, name) in _builder.QuarterRoofs)
            {
                _buildings.Add(new Building
                {
                    World = area,
                    Height = rise,
                    Roof = RoofFor(area, rise),
                    Title = string.IsNullOrEmpty(name) ? Quarter(area) : name,
                    Body = $"in the {Quarter(area)}\n" +
                           $"footprint  {area.width:F0} x {area.height:F0} m\n" +
                           $"height  {rise:F0} m",
                });
            }

            // Biggest first, so hierarchy order leaves the small footprints on top and
            // a shed against a tower block still takes its own click.
            _buildings.Sort((a, b) =>
                (b.World.width * b.World.height).CompareTo(a.World.width * a.World.height));
        }

        /// <summary>A roof for a building: rolled off where it stands, so the same
        /// house is the same colour every time the map is drawn, and leaning grey for
        /// the tall ones - a twenty-storey block is not a clapboard roof.</summary>
        static Color RoofFor(Bounds bounds)
        {
            int hash = RoofHash(new Vector2(bounds.center.x, bounds.center.z));
            return bounds.size.y > 26f ? Roofs[4 + hash % 2] : Roofs[hash % 4];
        }

        static int RoofHash(Vector2 at) =>
            Mathf.Abs(Mathf.RoundToInt(at.x * 7.3f) * 31 + Mathf.RoundToInt(at.y * 5.1f));

        /// <summary>A roof rolled off a footprint rather than a bounding box - the same
        /// arithmetic, for the houses taken off the city's own renderers.</summary>
        static Color RoofFor(Rect area, float rise)
        {
            int hash = RoofHash(area.center);
            return rise > 26f ? Roofs[4 + hash % 2] : Roofs[hash % 4];
        }

        // ------------------------------------------------------------- the houses

        /// <summary>Every roof the plan draws, in metres, biggest first.</summary>
        readonly List<(Rect area, Color tint)> _roofs = new List<(Rect, Color)>();

        /// <summary>One mesh of the plan and the ground it covers: drawn while the view
        /// is over that ground and switched off while it is not.</summary>
        sealed class Sheet
        {
            public Rect Ground;
            public GameObject Go;
            public bool On = true;

            /// <summary>Pixels to the metre below which this sheet is not drawn at all.
            /// A city's worth of centre-line dashes and litter bins seen from a mile up
            /// is a grey smear that hides the town it is drawn over and costs more than
            /// the town does - so the fine print comes on as the plan is walked into and
            /// goes off as it is pulled back.</summary>
            public float MinScale;
        }

        readonly List<Sheet> _sheets = new List<Sheet>();

        /// <summary>Roofs bucketed by ground, for the two questions asked of them: is
        /// this candidate already inside one, and does this footprint have any.</summary>
        readonly Dictionary<Vector2Int, List<Rect>> _roofGrid =
            new Dictionary<Vector2Int, List<Rect>>();

        /// <summary>
        /// The houses. Every renderer under the block roots that stands up and covers
        /// ground becomes a roof, biggest first, and anything that turns out to be
        /// mostly inside a roof already taken is dropped as a part of it - a window, a
        /// door, a sign, a tank on the roof. What is left is one small rectangle per
        /// building, which is what a block has to look like for the plan to read as a
        /// town rather than a car park.
        ///
        /// The same pass answers the other question the plan needs: which of the
        /// clickable footprints have houses of their own drawn inside them, and so
        /// should not be painted over the top of them.
        /// </summary>
        void CollectRoofs()
        {
            var found = new List<(Rect area, float rise, bool green)>();
            foreach (var renderer in _blockRoot.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (renderer == null) continue;
                var b = renderer.bounds;
                if (b.size.y < RoofRise) continue;                    // flat: ground, not a wall
                if (b.size.x < RoofSide || b.size.z < RoofSide) continue;   // a fence, a pole
                if (b.size.x * b.size.z < RoofArea) continue;
                found.Add((Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z),
                    b.size.y, Greenery(renderer.name)));
            }

            // Biggest first: a house is drawn before the balcony that hangs off it, so
            // it is the balcony that is swallowed and never the other way round.
            found.Sort((a, c) => (c.area.width * c.area.height)
                .CompareTo(a.area.width * a.area.height));

            foreach (var (area, rise, green) in found)
            {
                if (_roofs.Count >= RoofBudget) break;
                if (Swallowed(area)) continue;
                Keep(area);
                _roofs.Add((area, green ? Canopy : RoofFor(area, rise)));
            }

            for (int i = 0; i < _buildings.Count; i++)
                _buildings[i].Covered = AnyRoofIn(_buildings[i].World);

            // The buckets were scaffolding for those two questions and are tens of
            // thousands of rectangles; nothing asks them anything again.
            _roofGrid.Clear();

            int covered = 0;
            for (int i = 0; i < _buildings.Count; i++)
                if (_buildings[i].Covered) covered++;
            Debug.Log($"[RoadDemo] map: {_roofs.Count} roofs off {found.Count} standing " +
                      $"renderers, {covered} of {_buildings.Count} footprints drawn as houses");
        }

        /// <summary>What the map paints green instead of roofing: the city's own trees
        /// stand as tall as a house and would otherwise print as one.</summary>
        static bool Greenery(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.Contains("tree") || name.Contains("palm") || name.Contains("bush") ||
                   name.Contains("hedge") || name.Contains("shrub") || name.Contains("plant") ||
                   name.Contains("foliage");
        }

        static Vector2Int Bucket(float x, float z) =>
            new Vector2Int(Mathf.FloorToInt(x / RoofCell), Mathf.FloorToInt(z / RoofCell));

        void Keep(Rect area)
        {
            var lo = Bucket(area.xMin, area.yMin);
            var hi = Bucket(area.xMax, area.yMax);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                {
                    var key = new Vector2Int(x, y);
                    if (!_roofGrid.TryGetValue(key, out var here))
                        _roofGrid[key] = here = new List<Rect>();
                    here.Add(area);
                }
        }

        /// <summary>Is this footprint mostly inside a roof already drawn? Measured
        /// against the single largest overlap and not their sum - two roofs that share
        /// an edge must not add up to a verdict neither of them earns.</summary>
        bool Swallowed(Rect area)
        {
            float mine = area.width * area.height;
            if (mine <= 0f) return true;
            var lo = Bucket(area.xMin, area.yMin);
            var hi = Bucket(area.xMax, area.yMax);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                {
                    if (!_roofGrid.TryGetValue(new Vector2Int(x, y), out var here)) continue;
                    for (int i = 0; i < here.Count; i++)
                        if (Overlap(area, here[i]) >= RoofSwallow * mine)
                            return true;
                }
            return false;
        }

        /// <summary>Does any house stand inside this footprint - by its middle, which is
        /// the only part of a roof that certainly belongs to the building under it.</summary>
        bool AnyRoofIn(Rect footprint)
        {
            var lo = Bucket(footprint.xMin, footprint.yMin);
            var hi = Bucket(footprint.xMax, footprint.yMax);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                {
                    if (!_roofGrid.TryGetValue(new Vector2Int(x, y), out var here)) continue;
                    for (int i = 0; i < here.Count; i++)
                    {
                        var at = here[i].center;
                        if (at.x >= footprint.xMin && at.x <= footprint.xMax &&
                            at.y >= footprint.yMin && at.y <= footprint.yMax)
                            return true;
                    }
                }
            return false;
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w <= 0f || h <= 0f ? 0f : w * h;
        }

        // ----------------------------------------------------------- construction

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = FullOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            gameObject.AddComponent<GraphicRaycaster>();

            // The top bar and the book both bring one, but neither is guaranteed to
            // exist here - and a raycaster without an EventSystem never sees a click.
            if (!EventSystem.current)
            {
                var host = new GameObject("EventSystem");
                host.AddComponent<EventSystem>();
                host.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // The panel: the whole screen when the map IS the view, the book's empty
            // right half when it is docked. Its own face is a raycast target on
            // purpose - that is what stops a click landing on the city behind it.
            var panel = DemoUi.NewRect("Map Panel", transform);
            _panel = panel.gameObject;
            _panelRect = panel;

            _panelFace = panel.gameObject.AddComponent<Image>();
            _panelFace.raycastTarget = true;
            DemoUi.Dress(_panelFace, DemoUi.Box, 15f, DemoUi.Panel);

            BuildHeader(panel);

            var view = DemoUi.NewRect("View", panel);
            _view = view;
            DemoUi.Fill(view);
            view.gameObject.AddComponent<RectMask2D>();

            // The sea, under everything, and the click that clears a selection - the
            // same gesture as clicking bare street in the world. It is also what a
            // drag across open water is caught on.
            var ground = DemoUi.Block(view, "Sea", Sea);
            DemoUi.Fill(ground.rectTransform);
            ground.raycastTarget = true;
            var clear = ground.gameObject.AddComponent<MapZone>();
            clear.map = this;
            clear.index = -1;

            // Everything drawn in METRES lives in here; the view is a scale and an
            // offset on this one rect.
            _content = DemoUi.NewRect("Plan", view);
            _content.sizeDelta = Vector2.zero;

            // Bottom up: the country, the town's ground and its blocks, the houses
            // standing on them, the families' ground washed over the lot, the clickable
            // panes over the houses, and the lettering over everything.
            BuildLand(_content);
            BuildPlan(_content);
            // the pavements, the crossings, the paint and the furniture: over the
            // ground the blocks and the carriageways laid, under the houses standing
            // on it - a lamp post is not drawn on top of the house behind it
            BuildStreet(_content);
            BuildRoofs(_content);
            BuildTurf(_content);
            BuildFaces(_content);
            BuildLabels(_content);
            BuildIcons(view);
            BuildMovers(view);
            // A little dark in the corners, over the plan and the crowd both: the plan
            // is a lit table and the eye is meant to fall in the middle of it.
            BuildVignette(view);
            // and the outline of the ground the player is actually looking at
            BuildFrame(view);
            // Last children of the view, so they print over the plan and the crowd and
            // share the view's own coordinates - the card is placed off a footprint.
            BuildPopup(view);
            BuildCaption(view);

            ApplyMode(Mode.Off);
        }

        void BuildHeader(RectTransform panel)
        {
            var header = DemoUi.NewRect("Header", panel);
            _header = header.gameObject;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, HeaderHeight);

            var title = DemoUi.Text(header, "Title", 15f, DemoUi.Ink,
                TextAlignmentOptions.MidlineLeft, display: true);
            title.characterSpacing = 3f;
            title.text = "CITY MAP";
            var rect = title.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(ViewPad + 2f, 0f);
            rect.offsetMax = new Vector2(0f, -ViewPad);

            // The key, right-shouldered on the same line: what a dot is, what a
            // rectangle is, and what blue means.
            var legend = DemoUi.NewRect("Legend", header);
            legend.anchorMin = new Vector2(1f, 1f);
            legend.anchorMax = new Vector2(1f, 1f);
            legend.pivot = new Vector2(1f, 1f);
            legend.anchoredPosition = new Vector2(-ViewPad, -ViewPad);
            legend.sizeDelta = new Vector2(4f * LegendStep, HeaderHeight - ViewPad);

            LegendChip(legend, 0, DemoUi.Dot, new Vector2(9f, 9f), PoliceBlue, "POLICE");
            LegendChip(legend, 1, DemoUi.Dot, new Vector2(9f, 9f), DemoUi.Ink, "CIVILIAN");
            LegendChip(legend, 2, null, new Vector2(5f, 9f), DemoUi.Ink, "TRAFFIC");
            LegendChip(legend, 3, DemoUi.Dot, new Vector2(9f, 9f), DemoUi.Gold, "OUTFIT");
        }

        const float LegendStep = 86f;

        void LegendChip(RectTransform legend, int slot, Sprite sprite, Vector2 size,
            Color tint, string label)
        {
            var glyph = DemoUi.Block(legend, label + " Glyph", tint);
            glyph.sprite = sprite;
            glyph.preserveAspect = sprite != null;
            var glyphRect = glyph.rectTransform;
            glyphRect.anchorMin = new Vector2(0f, 0.5f);
            glyphRect.anchorMax = new Vector2(0f, 0.5f);
            glyphRect.pivot = new Vector2(0f, 0.5f);
            glyphRect.anchoredPosition = new Vector2(slot * LegendStep, 0f);
            glyphRect.sizeDelta = size;

            var text = DemoUi.Text(legend, label, 10.5f, DemoUi.InkDim,
                TextAlignmentOptions.MidlineLeft);
            text.characterSpacing = 2f;
            text.text = label;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(slot * LegendStep + 15f, 0f);
            rect.sizeDelta = new Vector2(LegendStep - 20f, 16f);
        }

        /// <summary>The line along the bottom of the full-screen map that says how to
        /// leave it. Nothing else on the plan explains itself.</summary>
        void BuildCaption(RectTransform view)
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                return;

            _caption = DemoUi.Text(view, "Caption", 15f, Ink,
                TextAlignmentOptions.BottomRight, display: true);
            _caption.characterSpacing = 3f;
            _caption.text = "WHEEL IN TO GO DOWN INTO THE STREET";
            var rect = _caption.rectTransform;
            rect.anchorMin = new Vector2(0.4f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-18f, 14f);
            rect.sizeDelta = new Vector2(0f, 22f);
        }

        // ----------------------------------------------------------- countryside

        /// <summary>
        /// The island around the town, sampled off the very heightfield the ground was
        /// built from and written into ONE mesh: land in two greens laid out in coarse
        /// fields, sand where the ground dips to the water, and nothing at all where
        /// the sea already shows through. Cells are merged along each row, so a mile
        /// of open country is a handful of quads rather than a thousand.
        /// </summary>
        void BuildLand(RectTransform content)
        {
            var area = _builder.IslandArea;
            if (area.width < 1f || area.height < 1f)
                return;

            // Cut into bands up the map, each its own mesh: standing in a street at the
            // south end of the island, the corner panel draws the band it is in and
            // leaves the other nine switched off.
            var country = DemoUi.NewRect("Country", content);
            MapPatches patches = null;
            float bandFrom = 0f;

            MapPatches Band(float from)
            {
                var host = DemoUi.NewRect("Band", country);
                host.anchoredPosition = area.center - _origin;
                host.sizeDelta = area.size;
                // The renderer BEFORE the graphic: RequireComponent is not inherited
                // through a Graphic subclass on every path, and a MaskableGraphic without
                // one throws inside RectMask2D's clipping - which aborts the whole
                // canvas update, and takes the entire map down with it.
                host.gameObject.AddComponent<CanvasRenderer>();
                var made = host.gameObject.AddComponent<MapPatches>();
                made.color = Color.white;
                _sheets.Add(new Sheet
                {
                    Ground = new Rect(area.xMin, from, area.width, LandBand),
                    Go = host.gameObject,
                });
                bandFrom = from;
                return made;
            }

            // the cell grows with the island: at twenty metres a six-kilometre island is
            // ninety thousand samples and far more merged runs than one UI mesh can
            // hold, and the far half of the country simply stopped being drawn
            float Cell = Mathf.Max(20f, Mathf.Sqrt(area.width * area.height) / 200f);
            // The city's own two lines and not the map's guess at them. They were a
            // guess, and it was wrong both ways: everything under -0.35 was painted SEA
            // (the water plane is nearly two and a half metres lower than that) and
            // everything under +0.55 was painted SAND - which is grass in the world, and
            // is most of the island. The plan read as a desert with a town in it.
            float sea = RoadDemoBuilder.WaterY;
            float shore = RoadDemoBuilder.BeachLine;
            const int Budget = 12000;         // quads: one UI mesh may not pass 65k verts

            int nx = Mathf.CeilToInt(area.width / Cell);
            int nz = Mathf.CeilToInt(area.height / Cell);
            int runs = 0;

            for (int j = 0; j < nz && runs < Budget; j++)
            {
                float z = area.yMin + j * Cell;
                if (patches == null || z - bandFrom >= LandBand || patches.Count >= LandPerMesh)
                    patches = Band(z);
                int open = -1;             // which cell the current run started at
                int kind = 0;              // 0 sea, 1 sand, 2 field, 3 the other field
                for (int i = 0; i <= nx; i++)
                {
                    int here = 0;
                    if (i < nx)
                    {
                        float x = area.xMin + i * Cell + Cell * 0.5f;
                        float cz = z + Cell * 0.5f;
                        // the town draws its own ground; no need to paint under it
                        if (!_world.Contains(new Vector2(x, cz)))
                        {
                            float h = _builder.LandHeight(x, cz);
                            here = h < sea ? 0 : h < shore ? 1 : Field2(x, cz);
                        }
                    }

                    if (here == kind)
                        continue;
                    if (kind != 0 && open >= 0)
                    {
                        patches.Add(Rect.MinMaxRect(
                                area.xMin + open * Cell - area.center.x,
                                z - area.center.y,
                                area.xMin + i * Cell - area.center.x,
                                z + Cell - area.center.y),
                            kind == 1 ? Shore : kind == 2 ? Field : FieldAlt);
                        runs++;
                    }
                    kind = here;
                    open = i;
                }
            }
        }

        /// <summary>Which of the two greens a patch of country wears: coarse squares of
        /// field, the way farmland reads from the air.</summary>
        static int Field2(float x, float z) =>
            (Mathf.FloorToInt(x / 140f) + Mathf.FloorToInt(z / 140f)) % 2 == 0 ? 2 : 3;

        /// <summary>The drawn city, bottom layer up: the town's own ground, the seams
        /// (the river, the park's lawn, the freeway's deck), the wide roads that read
        /// as avenues, the block slabs kerb to kerb, the quarters' washes, and the
        /// buildings standing on it all. Everything here is placed in METRES.</summary>
        void BuildPlan(RectTransform content)
        {
            Slot(content, "Town", _world, Asphalt);

            // the seams: the water the bridges cross, the park's lawn, the deck
            var seams = DemoUi.NewRect("Seams", content);
            foreach (var seam in _builder.SeamPlans)
                Slot(seams, seam.Kind.ToString(), seam.Area,
                    seam.Kind == SeamKind.River ? River :
                    seam.Kind == SeamKind.Highway ? Deck : Lawn);

            var roads = DemoUi.NewRect("Roads", content);
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            for (int i = 0; i < vx.Length; i++)
            {
                if (!_builder.verticalIsBoulevard[i])
                    continue;
                float half = _builder.VerticalHalfWidth(i);
                Slot(roads, "Avenue", Rect.MinMaxRect(vx[i] - half, _world.yMin,
                    vx[i] + half, _world.yMax), Avenue);
                Slot(roads, "Median", Rect.MinMaxRect(vx[i] - 0.7f, _world.yMin,
                    vx[i] + 0.7f, _world.yMax), Median);
            }
            for (int j = 0; j < hz.Length; j++)
            {
                if (!_builder.horizontalIsBoulevard[j])
                    continue;
                float half = _builder.HorizontalHalfWidth(j);
                Slot(roads, "Avenue", Rect.MinMaxRect(_world.xMin, hz[j] - half,
                    _world.xMax, hz[j] + half), Avenue);
                Slot(roads, "Median", Rect.MinMaxRect(_world.xMin, hz[j] - 0.7f,
                    _world.xMax, hz[j] + 0.7f), Median);
            }

            var blocks = DemoUi.NewRect("Blocks", content);
            // the yards first, under the lots: a built-over close is the ground between
            // two blocks that grew together, and drawn as anything but a block it would
            // put a road-coloured stripe down the middle of what the eye reads as one
            foreach (var yard in _builder.MergedYards)
                Slot(blocks, "Yard", yard, Slab);
            foreach (var lot in _builder.LotPlans)
            {
                // a pocket park is a lot with no building on it: the plan draws it the
                // colour of the parks, kerb ring and all, or a square of lawn would
                // come out as another block of houses
                Slot(blocks, "Block", lot.Slab, lot.Green ? Lawn : Slab);
                Slot(blocks, "Yard", lot.Interior, lot.Green ? Lawn : LotFace);
            }

            // the quarters, washed in their own colour over the ground they stand on,
            // and their houses over the wash - a quarter's buildings are not under
            // the Blocks root, so they come off the footprints it reported instead
            // and are drawn but not clickable
            var washes = DemoUi.NewRect("Quarters", content);

            // The ground a quarter made for itself, before anything of its own is drawn
            // on it: the yard the port paves and the water it had the island leave open.
            // Nobody else can report a basin - it is not a seam, not a block and not a
            // footprint - and a port drawn without its water is a car park.
            var ground = _builder.Reservations;
            if (ground != null)
            {
                var made = new List<(Rect area, Color tint)>();
                foreach (var paved in ground.Paved)
                    if (!Inside(_world, paved))
                        made.Add((paved, PavedAs(paved)));
                foreach (var water in ground.Water)
                    made.Add((water, Sea));
                SpillFill(DemoUi.NewRect("Quarter Ground", washes), made);
            }

            foreach (var district in _builder.DistrictPlans)
                Slot(washes, district.Name, district.World,
                    district.Kind == DistrictKind.Harbor ? HarborWash :
                    district.Kind == DistrictKind.Suburb ? SuburbWash : PadWash);

            // EVERY road, off the network the cars actually drive - the grid's streets,
            // the roads out to the quarters, the belt and its slip roads, a filling
            // station's apron - and not only the ones the plan could infer from the road
            // lines. A map with a road missing is a map that cannot be trusted.
            var tarmac = new List<(Vector2 a, Vector2 b, float half)>();
            var net = _builder.Net;
            if (net != null)
                foreach (var road in net.Roads)
                {
                    if (road == null) continue;
                    tarmac.Add((new Vector2(road.A.x, road.A.z), new Vector2(road.B.x, road.B.z),
                        Mathf.Max(3f, road.HalfRoad)));
                }
            foreach (var one in _builder.QuarterRoads)
                tarmac.Add(one);
            if (tarmac.Count > 0)
                SpillStrips(DemoUi.NewRect("Carriageways", washes), tarmac, Asphalt);
            Debug.Log($"[RoadDemo] map roads: {(net != null ? net.Roads.Count : 0)} on the " +
                      $"network, {_builder.QuarterRoads.Count} laid by or out to the quarters, " +
                      $"{_builder.QuarterRoofs.Count} quarter buildings");

            // Only the dark line under them: the roof itself is the clickable pane, drawn
            // with the city's own footprints in BuildFaces.
            var quarterLines = DemoUi.NewRect("Quarter Roof Lines", washes);
            var houses = new List<(Rect area, Color tint)>();
            foreach (var (area, rise, _) in _builder.QuarterRoofs)
                houses.Add((Grown(AtLeast(area, MinBuilding), RoofEdge), RoofLine));
            SpillFill(quarterLines, houses);
        }

        /// <summary>A footprint no thinner than the plan can print, kept on its own
        /// middle.</summary>
        static Rect AtLeast(Rect area, float least)
        {
            float w = Mathf.Max(area.width, least), h = Mathf.Max(area.height, least);
            return new Rect(area.center.x - w * 0.5f, area.center.y - h * 0.5f, w, h);
        }

        /// <summary>What a quarter's own ground is drawn in. A suburb PAVES its whole
        /// rectangle - lawns, drives and all - so painting that the grey of a dock apron
        /// turns every village into a car park; a port's yard and a works' pad really are
        /// asphalt. The kind is taken from the quarter the ground stands in.</summary>
        Color PavedAs(Rect area)
        {
            var at = area.center;
            foreach (var district in _builder.DistrictPlans)
                if (district.World.Contains(at))
                    return district.Kind == DistrictKind.Suburb ? LotFace : Deck;
            return LotFace;
        }

        /// <summary>Which quarter a piece of ground stands in, for the card on a shed
        /// that has no street to name.</summary>
        string Quarter(Rect area)
        {
            var at = area.center;
            foreach (var district in _builder.DistrictPlans)
                if (district.World.Contains(at))
                    return district.Name;
            return "town";
        }

        /// <summary>Is this rectangle wholly inside that one - the test that keeps the
        /// map from painting a quarter's ground over the town's own.</summary>
        static bool Inside(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin && inner.xMax <= outer.xMax &&
            inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;

        Vector2 Plan(Vector2 world) => world - _origin;

        // --------------------------------------------------------- the fine print
        //
        // A plan drawn to the blocks alone is a diagram: grey streets between grey
        // lots, and nothing in it a man could walk down. What makes it a MAP is the
        // detail the city already holds and the plan was throwing away - the pavement
        // the crowd walks on, the zebra it waits at, the paint down the middle of the
        // carriageway, the lamps and bins and trunks standing at the kerb, the trees.
        //
        // None of it is guessed: the pavements come off the pedestrian graph the crowd
        // itself routes on, the crossings off the gated links in that graph, the paint
        // off the lane network the cars drive, the furniture off the sidewalk plan every
        // prop claimed its ground in. If a walk is not on this map, the crowd cannot
        // walk it either.

        /// <summary>How deep a crossing is laid (BuildPedGraph lays it 5 m), how wide
        /// one bar of it is and how far apart the bars stand. Kept THIN and few: a
        /// crossing drawn as a full ladder of fat white rungs is the loudest thing on
        /// the plan, and it is not what the player is looking for.</summary>
        const float ZebraDeep = 5f, ZebraBar = 0.35f, ZebraGap = 1.6f;

        /// <summary>The broken lines on a carriageway: 4 m of paint, 5 m of nothing.
        /// The city paints its streets the way the reference photograph does - a yellow
        /// line down the middle and white lines between the lanes - so the plan paints
        /// the same two, and a street on the map is read the way the street is.</summary>
        const float DashOn = 4f, DashOff = 5f, DashHalf = 0.17f;

        /// <summary>The dark edge drawn under a pavement, so a walk reads as a kerb and
        /// not as a pale smear beside the road.</summary>
        const float KerbEdge = 0.5f;

        /// <summary>A carriageway wider than this has its median drawn already
        /// (BuildPlan) and wants no centre line of its own.</summary>
        const float BoulevardHalf = 10f;

        /// <summary>Pixels to the metre at which each layer of the fine print comes on.
        /// The pavements are the plan and are always drawn; paint and furniture are for
        /// the walked-into view.</summary>
        const float PaintScale = 0.45f, PropScale = 0.9f;

        /// <summary>What a mesh of dashes or bins may hold before another is started -
        /// they are small quads, so they may be packed far tighter than the roofs.</summary>
        const int FineBudget = 40000;

        void BuildStreet(RectTransform content)
        {
            var root = DemoUi.NewRect("Street", content);
            BuildPavements(root);
            BuildPaint(root);
            BuildFurniture(root);
        }

        /// <summary>
        /// The pavements and the crossings, off the crowd's own graph. Every stretch is
        /// in that graph TWICE (there and back), so one of each pair is drawn; every
        /// junction corner is a slab in its own right, or a plan of a crossroads would
        /// have four walks arriving at a hole.
        /// </summary>
        void BuildPavements(RectTransform root)
        {
            var walks = _builder.Pavement;
            if (walks == null || walks.Count == 0)
                return;

            float wide = RoadDemoBuilder.SidewalkWidth;
            float half = wide * 0.5f;
            var strips = new List<(Vector2 a, Vector2 b, float half)>();
            var kerbs = new List<(Vector2 a, Vector2 b, float half)>();
            var bars = new List<(Vector2 a, Vector2 b, float half)>();
            var corners = new List<(Rect area, Color tint)>();
            var slabs = new List<(Rect area, Color tint)>();
            var seen = new HashSet<PedNode>();
            int crossings = 0;
            int filed = _sheets.Count;

            for (int i = 0; i < walks.Count; i++)
            {
                var link = walks[i];
                if (link == null || link.From == null || link.To == null)
                    continue;

                if (seen.Add(link.From)) Corner(link.From, wide, corners, slabs);
                if (seen.Add(link.To)) Corner(link.To, wide, corners, slabs);

                var a = new Vector2(link.From.Pos.x, link.From.Pos.z);
                var b = new Vector2(link.To.Pos.x, link.To.Pos.z);
                // one of the pair, taken by the same rule every time
                if (a.x > b.x || (Mathf.Approximately(a.x, b.x) && a.y > b.y))
                    continue;

                if (link.Gated)
                {
                    Zebra(a, b, half, bars);
                    crossings++;
                    continue;
                }
                kerbs.Add((a, b, half + KerbEdge));
                strips.Add((a, b, half));
            }

            // bottom up: the kerb, the pavement over it, the corners over that (a corner
            // is where two walks meet and must not be cut by either one's edge), the
            // crossings last, since a zebra is painted ON the road it crosses
            SpillStrips(DemoUi.NewRect("Kerbs", root), kerbs, Kerb);
            SpillStrips(DemoUi.NewRect("Pavements", root), strips, Walk);
            SpillFill(DemoUi.NewRect("Corner Kerbs", root), corners);
            SpillFill(DemoUi.NewRect("Corners", root), slabs);
            // no zoom gate on the pavements - they ARE the plan - but they are still
            // filed off, so only the sheets the view is over are ever built
            Gate(filed, 0f);

            // the zebras are PAINT, and go on with the rest of the paint: pulled back,
            // every crossing in town at once is a white comb over the whole grid
            filed = _sheets.Count;
            SpillStrips(DemoUi.NewRect("Crossings", root), bars, ZebraPaint);
            Gate(filed, PaintScale);

            Debug.Log($"[RoadDemo] map street: {strips.Count} stretches of pavement, " +
                      $"{crossings} crossings, {slabs.Count} corner slabs");
        }

        /// <summary>The slab a junction corner is: the walk down one street meets the
        /// walk down the other over this square, and neither draws it.</summary>
        static void Corner(PedNode node, float wide,
            List<(Rect area, Color tint)> kerbs, List<(Rect area, Color tint)> slabs)
        {
            var at = new Vector2(node.Pos.x, node.Pos.z);
            var slab = new Rect(at.x - wide * 0.5f, at.y - wide * 0.5f, wide, wide);
            kerbs.Add((Grown(slab, KerbEdge), Kerb));
            slabs.Add((slab, Walk));
        }

        /// <summary>One zebra, laid the way the city paints it: the bars run ACROSS the
        /// walker's path - along the way the cars drive - and repeat down the length of
        /// the crossing. Bars along the walk instead give two or three long rails over
        /// the road, which is not a crossing and is not what the street looks like.
        /// Stopped short of both kerbs, so the paint stays on the carriageway.</summary>
        static void Zebra(Vector2 a, Vector2 b, float pavementHalf,
            List<(Vector2 a, Vector2 b, float half)> bars)
        {
            var span = b - a;
            float len = span.magnitude;
            if (len <= pavementHalf * 2f + 0.5f)
                return;
            var dir = span / len;
            var side = new Vector2(-dir.y, dir.x) * (ZebraDeep * 0.5f);
            float across = len - pavementHalf * 2f;
            // the run of bars centred on the crossing, so the gaps fall either side of
            // the middle of the road and not on it
            int count = Mathf.Max(2, Mathf.FloorToInt(across / ZebraGap));
            float slack = (across - (count - 1) * ZebraGap) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var at = a + dir * (pavementHalf + slack + i * ZebraGap);
                bars.Add((at - side, at + side, ZebraBar));
            }
        }

        /// <summary>The paint on the road, laid the way the city lays it: a yellow line
        /// down the middle of the carriageway and a white line between every pair of
        /// lanes, out to the kerb strip cars park on. A boulevard's middle is a MEDIAN
        /// and is drawn already (BuildPlan); its lanes still get their white lines.
        /// </summary>
        void BuildPaint(RectTransform root)
        {
            var net = _builder.Net;
            if (net == null)
                return;

            var middle = new List<(Vector2 a, Vector2 b, float half)>();
            var lanes = new List<(Vector2 a, Vector2 b, float half)>();
            foreach (var road in net.Roads)
            {
                if (road == null)
                    continue;
                var a = new Vector2(road.A.x, road.A.z);
                var b = new Vector2(road.B.x, road.B.z);
                var span = b - a;
                float len = span.magnitude;
                if (len < DashOn + DashOff * 2f)
                    continue;
                var dir = span / len;
                var side = new Vector2(-dir.y, dir.x);

                if (road.HalfRoad <= BoulevardHalf)
                    Dashes(a, dir, len, Vector2.zero, middle);

                // one white line per lane boundary, walked out from the middle: the
                // outermost is the edge of the strip a car is left standing on, which
                // is where the world paints its own outside line
                for (float off = road.HalfRoad - StreetKit.ParkLane;
                     off > StreetKit.RoadHalf * 0.5f; off -= StreetKit.RoadHalf)
                {
                    Dashes(a, dir, len, side * off, lanes);
                    Dashes(a, dir, len, side * -off, lanes);
                }
            }

            int filed = _sheets.Count;
            SpillStrips(DemoUi.NewRect("Lane Lines", root), lanes, LanePaint);
            SpillStrips(DemoUi.NewRect("Centre Line", root), middle, Median);
            Gate(filed, PaintScale);
            Debug.Log($"[RoadDemo] map street: {middle.Count} middle dashes, " +
                      $"{lanes.Count} lane dashes");
        }

        /// <summary>One broken line down a carriageway, offset from its centre.</summary>
        static void Dashes(Vector2 from, Vector2 dir, float len, Vector2 offset,
            List<(Vector2 a, Vector2 b, float half)> into)
        {
            var start = from + offset;
            for (float t = DashOff; t + DashOn < len - DashOff; t += DashOn + DashOff)
                into.Add((start + dir * t, start + dir * (t + DashOn), DashHalf));
        }

        /// <summary>
        /// What stands at the kerb. Every prop the builder laid claimed its measured
        /// footprint in the sidewalk plan - that is how the walkers know to step round
        /// a lamp post - so the plan of the town can be drawn from the same register
        /// rather than from a second guess at where a bin might be.
        /// </summary>
        void BuildFurniture(RectTransform root)
        {
            var plan = _builder.Furniture;
            if (plan != null && plan.Count > 0)
            {
                var props = new List<(Vector2 a, Vector2 b, float half)>();
                foreach (var box in plan.Boxes)
                {
                    // a reservation is ground kept CLEAR - a crossing's mouth, the
                    // turning room on a corner - and nothing stands on it to draw
                    if (box.KeepClear)
                        continue;
                    if (props.Count >= FineBudget)
                        break;
                    var along = box.Ax * Mathf.Max(0.22f, box.H.x);
                    props.Add((box.C - along, box.C + along, Mathf.Max(0.22f, box.H.y)));
                }
                int at = _sheets.Count;
                SpillStrips(DemoUi.NewRect("Furniture", root), props, PropInk);
                Gate(at, PropScale);
                Debug.Log($"[RoadDemo] map street: {props.Count} props at the kerb");
            }

            // The city's trees are NOT drawn. The pass that did it is gone rather
            // than tuned: a plan of a downtown wants the streets, the walks and the
            // blocks, and every palm and yard tree in the grid printed over them is a
            // green rash that hides all three - which is what it looked like the one
            // time it was tried. The parks and the lawns keep their green, drawn as
            // GROUND (the seams and a green lot in BuildPlan), which is what a map of
            // a park is.
        }

        /// <summary>Hold every sheet made since <paramref name="from"/> back until the
        /// plan is drawn this close - the zoom gate on a layer of the fine print.
        ///
        /// They are switched OFF as they are filed, whatever the gate: a sheet's mesh is
        /// generated the first time its canvas updates with the thing active, and the
        /// fine print is a hundred thousand quads. Left on, every one of them would be
        /// built in the frame the map first comes up - for ground the view is nowhere
        /// near. Relayout turns on the handful the view is actually over.</summary>
        void Gate(int from, float minScale)
        {
            for (int i = from; i < _sheets.Count; i++)
            {
                var sheet = _sheets[i];
                sheet.MinScale = minScale;
                sheet.On = false;
                sheet.Go.SetActive(false);
            }
        }

        /// <summary>
        /// The houses, in as few meshes as the count allows: every roof's dark line
        /// first and every roof over the top of them, so a house that touches its
        /// neighbour is not roofed over by the neighbour's own outline. One Image per
        /// house would be twenty thousand objects for a city nobody clicks a window of;
        /// this is a handful of meshes that only ever get scaled.
        /// </summary>
        void BuildRoofs(RectTransform content)
        {
            if (_roofs.Count == 0)
                return;

            var lines = DemoUi.NewRect("Roof Lines", content);
            var faces = DemoUi.NewRect("Roofs", content);
            SpillRects(lines, faces, _roofs, RoofEdge);

            // They are in the meshes now, and the meshes are what the plan is.
            _roofs.Clear();
            _roofs.TrimExcess();
        }

        /// <summary>
        /// Roofs into meshes: every roof's dark line first and every roof over the top of
        /// them, so a house that touches its neighbour is not roofed over by the
        /// neighbour's own outline.
        ///
        /// Bucketed by GROUND and not by the order they were found in: a mesh that covers
        /// one quarter of town can be switched off while the map is looking at another,
        /// which is what makes the corner panel cost a corner panel and not the whole
        /// city every frame.
        /// </summary>
        void SpillRects(Transform lines, Transform faces,
            List<(Rect area, Color tint)> items, float edge)
        {
            var cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                var at = items[i].area.center;
                var key = new Vector2Int(Mathf.FloorToInt(at.x / SheetCell),
                    Mathf.FloorToInt(at.y / SheetCell));
                if (!cells.TryGetValue(key, out var here))
                    cells[key] = here = new List<int>();
                here.Add(i);
            }

            foreach (var cell in cells)
                Spill(lines, cell.Key, cell.Value, items, edge, true);
            foreach (var cell in cells)
                Spill(faces, cell.Key, cell.Value, items, 0f, false);
        }

        /// <summary>Flat rectangles with no line under them - a quarter's own paving and
        /// its water - bucketed by ground the same way.</summary>
        void SpillFill(Transform parent, List<(Rect area, Color tint)> items)
        {
            var cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                var at = items[i].area.center;
                var key = new Vector2Int(Mathf.FloorToInt(at.x / SheetCell),
                    Mathf.FloorToInt(at.y / SheetCell));
                if (!cells.TryGetValue(key, out var here))
                    cells[key] = here = new List<int>();
                here.Add(i);
            }
            foreach (var cell in cells)
                Spill(parent, cell.Key, cell.Value, items, 0f, false);
        }

        /// <summary>The quarters' streets: lines with a width, bucketed by the ground
        /// their middle stands on. A rectangle round a cell is not quite the ground a
        /// long street covers, which is what SheetPad in the cull is for.</summary>
        void SpillStrips(Transform parent,
            IReadOnlyList<(Vector2 a, Vector2 b, float half)> roads, Color tint)
        {
            var cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var at = (roads[i].a + roads[i].b) * 0.5f;
                var key = new Vector2Int(Mathf.FloorToInt(at.x / SheetCell),
                    Mathf.FloorToInt(at.y / SheetCell));
                if (!cells.TryGetValue(key, out var here))
                    cells[key] = here = new List<int>();
                here.Add(i);
            }

            foreach (var cell in cells)
            {
                // A street may run the length of its quarter, so the cell it is filed
                // under is grown to what its own line actually covers.
                var ground = new Rect(cell.Key.x * SheetCell, cell.Key.y * SheetCell,
                    SheetCell, SheetCell);
                MapPatches sheet = null;
                Sheet filed = null;
                foreach (int i in cell.Value)
                {
                    if (sheet == null || sheet.Count >= RoofsPerMesh)
                    {
                        sheet = NewSheet(parent);
                        filed = new Sheet { Ground = ground, Go = sheet.gameObject };
                        _sheets.Add(filed);
                    }
                    var (a, b, half) = roads[i];
                    sheet.Add(Plan(a), Plan(b), half, tint);
                    ground.xMin = Mathf.Min(ground.xMin, Mathf.Min(a.x, b.x) - half);
                    ground.xMax = Mathf.Max(ground.xMax, Mathf.Max(a.x, b.x) + half);
                    ground.yMin = Mathf.Min(ground.yMin, Mathf.Min(a.y, b.y) - half);
                    ground.yMax = Mathf.Max(ground.yMax, Mathf.Max(a.y, b.y) + half);
                    filed.Ground = ground;
                }
            }
        }

        /// <summary>One cell's roofs, in as many meshes as the vertex ceiling wants.</summary>
        void Spill(Transform parent, Vector2Int cell, List<int> mine,
            List<(Rect area, Color tint)> items, float grow, bool line)
        {
            var ground = new Rect(cell.x * SheetCell, cell.y * SheetCell, SheetCell, SheetCell);
            MapPatches sheet = null;
            Sheet filed = null;
            for (int i = 0; i < mine.Count; i++)
            {
                if (sheet == null || sheet.Count >= RoofsPerMesh)
                {
                    sheet = NewSheet(parent);
                    filed = new Sheet { Ground = ground, Go = sheet.gameObject };
                    _sheets.Add(filed);
                }
                var (area, tint) = items[mine[i]];
                sheet.Add(grow > 0f ? Grown(Plan(area), grow) : Plan(area),
                    line ? RoofLine : tint);
                // A rectangle is filed by its MIDDLE, and a port's basin filed by its
                // middle is a kilometre of water in a cell three hundred metres across:
                // the cull has to know the ground the mesh actually covers.
                ground.xMin = Mathf.Min(ground.xMin, area.xMin);
                ground.xMax = Mathf.Max(ground.xMax, area.xMax);
                ground.yMin = Mathf.Min(ground.yMin, area.yMin);
                ground.yMax = Mathf.Max(ground.yMax, area.yMax);
                filed.Ground = ground;
            }
        }

        /// <summary>One more mesh of flat rectangles, laid over the whole town.</summary>
        MapPatches NewSheet(Transform parent, string name = "Sheet")
        {
            var host = DemoUi.NewRect(name, parent);
            host.anchoredPosition = Vector2.zero;
            // The WHOLE plan and not the grid: a mask culls a graphic by its own
            // rectangle, not by the mesh inside it, so a sheet sized to the town went out
            // the moment the view was over a suburb - the quarters' streets and houses
            // simply were not there. What is drawn where is this map's own business
            // (the sheet cull in Relayout), not the mask's.
            host.sizeDelta = _paper.size;
            // The renderer BEFORE the graphic - see BuildLand: a MaskableGraphic without
            // one throws inside RectMask2D and takes the whole canvas update with it.
            host.gameObject.AddComponent<CanvasRenderer>();
            var patches = host.gameObject.AddComponent<MapPatches>();
            patches.color = Color.white;
            return patches;
        }

        /// <summary>A world rectangle in the plan's own coordinates - metres from the
        /// map's origin, which is where a sheet's middle sits.</summary>
        Rect Plan(Rect world) => new Rect(world.x - _origin.x, world.y - _origin.y,
            world.width, world.height);

        static Rect Grown(Rect area, float by) => Rect.MinMaxRect(
            area.xMin - by, area.yMin - by, area.xMax + by, area.yMax + by);


        /// <summary>
        /// The clickable footprints: one pane per catalog bake, over the houses drawn
        /// inside it. A bake whose own houses are on the plan is left CLEAR - painting
        /// it would put a seventy-metre slab back over the row of roofs that is the
        /// whole point - and only lights up under the pointer or under the card. A bake
        /// with nothing standing in it (a yard, a lot the kit left empty) keeps its own
        /// roof colour, or it would be a hole in the plan.
        /// </summary>
        void BuildFaces(RectTransform content)
        {
            var buildings = DemoUi.NewRect("Buildings", content);
            for (int i = 0; i < _buildings.Count; i++)
            {
                var building = _buildings[i];
                var face = Slot(buildings, building.Title, building.World, Idle(building),
                    MinBuilding);
                face.raycastTarget = true;
                building.Face = face;

                var zone = face.gameObject.AddComponent<MapZone>();
                zone.map = this;
                zone.index = i;
            }
        }

        static Color Idle(Building building) => building.Covered ? Ghost : building.Roof;

        /// <summary>How big a mark is drawn for the mode the map is in.</summary>
        Vector3 MarkScale => _mode == Mode.Corner
            ? new Vector3(CornerMarks, CornerMarks, 1f) : Vector3.one;

        // ---------------------------------------------------------- the families

        /// <summary>How far a family's ground reaches from its own door. A block whose
        /// middle is further than this from every front is nobody's.</summary>
        const float TurfReach = 240f;

        /// <summary>The wash a family's ground wears - light enough that the streets
        /// and the houses under it still read, strong enough to see whose quarter it is
        /// at a glance, which is what the original's colour is for.</summary>
        const float TurfAlpha = 0.28f;

        MapPatches _turf;
        int _turfStamp = -1;
        float _turfDue;

        void BuildTurf(RectTransform content) => _turf = NewSheet(content, "Turf");

        /// <summary>
        /// Whose city this is, block by block: every lot takes the colour of the family
        /// whose nearest door it stands by. The fronts are seated after the map is
        /// built (and can burn down later), so the wash is re-laid whenever the roll of
        /// families changes and never otherwise - it is one mesh either way.
        /// </summary>
        void RefreshTurf()
        {
            if (_turf == null || _builder == null)
                return;

            var fronts = GangFront.All;
            int stamp = fronts.Count;
            for (int i = 0; i < fronts.Count; i++)
                if (fronts[i] != null)
                    stamp = stamp * 31 + fronts[i].GangId;
            if (stamp == _turfStamp)
                return;
            _turfStamp = stamp;

            _turf.Clear();
            if (fronts.Count == 0)
                return;

            foreach (var lot in _builder.LotPlans)
            {
                var at = lot.Slab.center;
                int gang = -1;
                float best = TurfReach * TurfReach;
                for (int i = 0; i < fronts.Count; i++)
                {
                    var front = fronts[i];
                    if (front == null) continue;
                    float dx = front.Door.x - at.x, dz = front.Door.z - at.y;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= best) continue;
                    best = d2;
                    gang = front.GangId;
                }
                if (gang < 0)
                    continue;
                var tint = LivingCity.UI.GangPalette.Of(gang);
                tint.a = TurfAlpha;
                _turf.Add(Plan(lot.Slab), tint);
            }
        }

        // ------------------------------------------------------------- the trades

        /// <summary>What a building is, printed on it: the plaque and its letter. Only
        /// the places a player looks for - the civic buildings and the trades - are
        /// marked, because a mark on every shop is a mark on nothing.</summary>
        static readonly (string needle, string mark, Color tint)[] Trades =
        {
            ("hospital", "+", new Color(0.80f, 0.15f, 0.15f)),
            ("policestation", "P", new Color(0.11f, 0.31f, 0.78f)),
            ("firestation", "F", new Color(0.85f, 0.33f, 0.09f)),
            ("bank", "$", new Color(0.48f, 0.38f, 0.06f)),
            ("school", "S", new Color(0.24f, 0.44f, 0.24f)),
            ("post", "M", new Color(0.20f, 0.34f, 0.55f)),
            ("casino", "7", new Color(0.62f, 0.16f, 0.45f)),
            ("hotel", "H", new Color(0.36f, 0.28f, 0.55f)),
            ("restaurant", "R", new Color(0.55f, 0.24f, 0.12f)),
            ("coffeeshop", "C", new Color(0.42f, 0.28f, 0.14f)),
            ("cafe", "C", new Color(0.42f, 0.28f, 0.14f)),
            ("burger", "B", new Color(0.65f, 0.35f, 0.10f)),
            ("carwash", "A", new Color(0.16f, 0.40f, 0.44f)),
        };

        /// <summary>The plaque's size ON SCREEN. It does not grow with the ground: a
        /// hospital is a hospital at every zoom, which is how the original's map is
        /// read at a glance from the far end of the wheel.</summary>
        const float IconPx = 21f;

        static readonly Color Plaque = new Color(0.965f, 0.949f, 0.898f, 0.96f);

        sealed class Trade
        {
            public Vector2 At;
            public RectTransform Rect;
        }

        readonly List<Trade> _trades = new List<Trade>();

        void BuildIcons(RectTransform view)
        {
            var root = DemoUi.NewRect("Trades", view);
            DemoUi.Fill(root);

            foreach (var building in _buildings)
            {
                string name = building.Title == null ? "" : building.Title.ToLowerInvariant();
                string mark = null;
                var tint = Ink;
                for (int i = 0; i < Trades.Length; i++)
                {
                    if (!name.Contains(Trades[i].needle)) continue;
                    mark = Trades[i].mark;
                    tint = Trades[i].tint;
                    break;
                }
                if (mark == null)
                    continue;

                var plaque = DemoUi.Block(root, building.Title, Plaque);
                DemoUi.Dress(plaque, DemoUi.Box, 8f, Plaque);
                plaque.raycastTarget = false;
                plaque.rectTransform.sizeDelta = new Vector2(IconPx, IconPx);

                if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
                {
                    var letter = DemoUi.Text(plaque.rectTransform, "Mark", IconPx * 0.72f,
                        tint, TextAlignmentOptions.Center, display: true);
                    letter.text = mark;
                    letter.raycastTarget = false;
                    DemoUi.Fill(letter.rectTransform);
                }

                _trades.Add(new Trade
                {
                    At = building.World.center,
                    Rect = plaque.rectTransform,
                });
            }
        }

        /// <summary>One flat rectangle of the plan, in metres.</summary>
        Image Slot(Transform parent, string name, Rect world, Color tint, float floor = 0f)
        {
            var image = DemoUi.Block(parent, name, tint);
            var rect = image.rectTransform;
            rect.anchoredPosition = world.center - _origin;
            rect.sizeDelta = new Vector2(Mathf.Max(world.width, floor),
                Mathf.Max(world.height, floor));
            return image;
        }

        // ------------------------------------------------------------ the letters

        /// <summary>
        /// The lettering, which is most of what makes the original's map look like a
        /// map: the town's name across the middle, the quarters named over their own
        /// wash, and every street named ALONG itself - repeated down its length, so a
        /// name is near wherever the player has panned to, turned to read up the page
        /// on the north-south lines.
        /// </summary>
        void BuildLabels(RectTransform content)
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                return;

            var names = _builder.Streets;
            var root = _names = DemoUi.NewRect("Names", content);

            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            for (int i = 0; i < vx.Length; i++)
            {
                string name = names.Vertical(i);
                if (string.IsNullOrEmpty(name)) continue;
                bool grand = _builder.verticalIsBoulevard[i];
                float type = grand ? AvenueType : StreetType;
                float gap = Gap(vx, i);
                int ordinal = 0;
                // not where the street is not: a name lettered along the whole line
                // prints itself across the river, through the parks and down the middle
                // of every close, and half the city's streets stop somewhere now
                for (float z = _world.yMin + LabelStep * 0.5f; z < _world.yMax; z += LabelStep)
                {
                    if (!_builder.StreetOpenAt(true, i, z)) continue;
                    Letter(root, name, new Vector2(vx[i], z), 90f, type,
                        grand ? MinTypePx * 0.75f : MinTypePx, MaxTypePx,
                        grand ? AvenueTypePx : StreetTypePx, ordinal++, gap);
                }
            }
            for (int j = 0; j < hz.Length; j++)
            {
                string name = names.Horizontal(j);
                if (string.IsNullOrEmpty(name)) continue;
                bool grand = _builder.horizontalIsBoulevard[j];
                float type = grand ? AvenueType : StreetType;
                float gap = Gap(hz, j);
                int ordinal = 0;
                for (float x = _world.xMin + LabelStep * 0.5f; x < _world.xMax; x += LabelStep)
                {
                    if (!_builder.StreetOpenAt(false, j, x)) continue;
                    Letter(root, name, new Vector2(x, hz[j]), 0f, type,
                        grand ? MinTypePx * 0.75f : MinTypePx, MaxTypePx,
                        grand ? AvenueTypePx : StreetTypePx, ordinal++, gap);
                }
            }

            // the city's own quarters - the named parts of the grid - under the places
            // out of town, and off the plan again when it is close enough to read the
            // street names off instead
            foreach (var quarter in _builder.CityQuarters)
            {
                string qn = quarter.Name.ToUpperInvariant();
                float qwide = Mathf.Max(60f, quarter.World.width * 0.86f);
                float qtype = Mathf.Clamp(qwide / Mathf.Max(4, qn.Length) / 0.62f, 13f, QuarterType);
                Letter(root, qn, quarter.World.center, 0f, qtype, 9f, QuarterMaxPx);
            }

            foreach (var district in _builder.DistrictPlans)
            {
                string name = district.Name.ToUpperInvariant();
                // cut the type to the ground the place stands on. A dozen villages the
                // size of nine blocks carry names of their own now - CRANBERRY FLATS on a
                // quarter two hundred and seventy metres wide, set at the old fixed size,
                // ran out over the woods either side and across its neighbours
                float wide = Mathf.Max(60f, district.World.width * 0.92f);
                float type = Mathf.Clamp(wide / Mathf.Max(4, name.Length) / 0.62f, 16f, DistrictType);
                Letter(root, name, district.World.center, 0f, type, 9f);

                // What the place IS, under its name: a village names itself and needs no
                // explaining, but a port and an airfield are the two places on the plan a
                // player looks for by trade rather than by name.
                string trade = KindName(district.Kind);
                if (trade != null)
                    Letter(root, trade,
                        new Vector2(district.World.center.x, district.World.center.y - type * 1.15f),
                        0f, type * 0.46f, 8f);
            }

            // The town's own name across the middle of the grid, the way the original
            // prints its city across its map - and taken off again once the map is
            // close enough for the streets to carry their own names.
            Letter(root, names.City, _world.center, 0f, CityType, 6f, 70f);
        }

        /// <summary>One name on the plan: the ink, and a pale copy behind it so black
        /// letters still read over a dark green field or over the river.</summary>
        /// <summary>What a quarter is called on the plan when its name does not say -
        /// null for a village, which is only ever itself.</summary>
        static string KindName(DistrictKind kind) => kind switch
        {
            DistrictKind.Harbor => "PORT",
            DistrictKind.Airport => "AIRFIELD",
            DistrictKind.Pad => "WORKS",
            _ => null,
        };

        /// <summary>How much ground a line of the grid has to itself: the nearer of its
        /// two neighbours, and its own distance to the edge when it has only one.</summary>
        static float Gap(float[] lines, int at)
        {
            float gap = float.MaxValue;
            if (at > 0) gap = Mathf.Min(gap, Mathf.Abs(lines[at] - lines[at - 1]));
            if (at < lines.Length - 1) gap = Mathf.Min(gap, Mathf.Abs(lines[at + 1] - lines[at]));
            return gap == float.MaxValue ? 120f : gap;
        }

        void Letter(Transform parent, string text, Vector2 at, float turn, float metres,
            float minPx, float maxPx = MaxTypePx, float screenPx = 0f, int ordinal = 0,
            float gapMetres = 0f)
        {
            var host = DemoUi.NewRect(text, parent);
            host.anchoredPosition = at - _origin;
            host.sizeDelta = new Vector2(Mathf.Max(60f, text.Length * metres), metres * 2.2f);
            host.localRotation = Quaternion.Euler(0f, 0f, turn);

            var halo = DemoUi.Text(host, "Halo", metres, Halo,
                TextAlignmentOptions.Center, display: true);
            halo.text = text;
            halo.characterSpacing = 6f;
            halo.overflowMode = TextOverflowModes.Overflow;
            DemoUi.Fill(halo.rectTransform);
            halo.rectTransform.anchoredPosition = new Vector2(metres * 0.09f, -metres * 0.09f);

            var ink = DemoUi.Text(host, "Ink", metres, Ink,
                TextAlignmentOptions.Center, display: true);
            ink.text = text;
            ink.characterSpacing = 6f;
            ink.overflowMode = TextOverflowModes.Overflow;
            DemoUi.Fill(ink.rectTransform);

            _labels.Add(new Label
            {
                Rect = host, Metres = metres, MinPx = minPx, MaxPx = maxPx,
                ScreenPx = screenPx, Ordinal = ordinal, GapMetres = gapMetres,
            });
        }

        // ------------------------------------------------------------- the crowd

        void BuildMovers(RectTransform view)
        {
            var root = _moverRoot = DemoUi.NewRect("Movers", view);
            DemoUi.Fill(root);

            if (_civilians != null)
                foreach (var civilian in _civilians)
                    AddMover(root, civilian.Tf, null, false, CivilianInk, PedDot, PedDot,
                        DemoUi.Dot, civilian: true);
            if (_cars != null)
                foreach (var car in _cars)
                    AddMover(root, car.Tf, null, true, TrafficInk, CarWidth, CarLength, null);
            if (_officers != null)
                foreach (var officer in _officers)
                    AddMover(root, officer.Tf, officer, false, PoliceBlue, PoliceDot,
                        PoliceDot, DemoUi.Dot);
            if (_policeCars != null)
                foreach (var car in _policeCars)
                    AddMover(root, car.Tf, car, true, PoliceBlue, CarWidth, CarLength, null);
        }

        void AddMover(RectTransform root, Transform tf, IPatrolMarker patrol, bool vehicle,
            Color tint, float width, float height, Sprite sprite, bool civilian = false)
        {
            if (tf == null)
                return;

            var image = DemoUi.Block(root, vehicle ? "car" : "dot", tint);
            image.sprite = sprite;
            image.rectTransform.sizeDelta = new Vector2(width, height);
            image.enabled = false;
            _movers.Add(new Mover
            {
                Tf = tf, Img = image, Patrol = patrol, Vehicle = vehicle, Tint = tint,
                Civilian = civilian, Floor = new Vector2(width, height),
            });
        }

        // -------------------------------------------------- what the camera sees

        /// <summary>The outline of the ground in shot, in pixels of the panel, and what
        /// it is drawn in.</summary>
        const float FrameThick = 2f, FrameReach = 4000f;
        static readonly Color FrameInk = new Color(0.984f, 0.965f, 0.902f, 0.85f);

        Image[] _frame;
        readonly Vector2[] _frameAt = new Vector2[4];

        /// <summary>Four thin rules, one per side of the shot. Built once and moved:
        /// the outline changes shape every frame the camera turns or the boom moves,
        /// and a rebuilt mesh per frame for four lines is four allocations a frame for
        /// nothing.</summary>
        void BuildFrame(RectTransform view)
        {
            _frame = new Image[4];
            for (int i = 0; i < _frame.Length; i++)
            {
                var rule = DemoUi.Block(view, "shot", FrameInk);
                rule.enabled = false;
                _frame[i] = rule;
            }
        }

        /// <summary>
        /// Where the player is looking, drawn on the plan: the corner panel is a map of
        /// three hundred metres and the camera is somewhere inside it, so without this
        /// the panel says WHERE the district is but not which part of it is on the
        /// screen in front of him.
        ///
        /// The shape is taken from the camera itself - the four corners of the frame
        /// cast down onto the ground the city stands on - and not worked out from the
        /// boom: the camera tilts, so what it holds is a trapezium and never the
        /// rectangle a boom-and-scale sum would draw. It is not drawn on the FULL plan,
        /// where the camera is held and renders nothing at all.
        /// </summary>
        void PlotFrame()
        {
            if (_frame == null)
                return;
            bool want = _cam != null && _scale > 0f &&
                        (_mode == Mode.Corner || _mode == Mode.Docked);
            if (want)
                for (int i = 0; i < 4; i++)
                {
                    // round the frame: bottom left, bottom right, top right, top left
                    var at = new Vector2(i == 1 || i == 2 ? 1f : 0f, i >= 2 ? 1f : 0f);
                    if (!OnGround(at, out _frameAt[i]))
                    {
                        want = false;
                        break;
                    }
                }
            if (!want)
            {
                for (int i = 0; i < _frame.Length; i++)
                    if (_frame[i] != null && _frame[i].enabled)
                        _frame[i].enabled = false;
                return;
            }

            for (int i = 0; i < 4; i++)
                Rule(_frame[i], _frameAt[i], _frameAt[(i + 1) & 3]);
        }

        /// <summary>Where a corner of the screen lands on the ground the city stands
        /// on. False when it lands on the sky, or so far off that the camera is looking
        /// at the horizon and the answer means nothing.</summary>
        bool OnGround(Vector2 viewport, out Vector2 at)
        {
            at = default;
            var ray = _cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            if (ray.direction.y > -0.001f)
                return false;
            float run = -ray.origin.y / ray.direction.y;
            if (run <= 0f || run > FrameReach)
                return false;
            var point = ray.GetPoint(run);
            at = new Vector2(point.x, point.z);
            return true;
        }

        /// <summary>One side of the outline: a thin rect laid between two points of the
        /// panel and turned to face along them.</summary>
        void Rule(Image rule, Vector2 from, Vector2 to)
        {
            if (rule == null)
                return;
            var a = ToView(from);
            var b = ToView(to);
            var along = b - a;
            var rect = rule.rectTransform;
            rect.anchoredPosition = (a + b) * 0.5f;
            rect.sizeDelta = new Vector2(along.magnitude + FrameThick, FrameThick);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
            if (!rule.enabled)
                rule.enabled = true;
        }

        // ------------------------------------------------------------ the vignette

        /// <summary>Where the shade starts, as a fraction of the way out to the corner,
        /// and how dark it ever gets. Kept gentle: this is a plan being read, not a
        /// photograph, and a heavy vignette eats the streets at the edge of the view -
        /// which on this map is where the player is panning TO.</summary>
        const float VignetteClear = 0.52f, VignetteInk = 0.30f;

        Image _vignette;
        Texture2D _vignetteTex;

        /// <summary>
        /// The shade round the edge of the plan. One stretched sprite rather than a
        /// shader: the map is a UI canvas, and a 128 px ramp scaled over the view is
        /// smooth at any size a screen comes in and costs one transparent quad.
        ///
        /// It never takes a click - the plan under it is clicked, panned and dragged,
        /// and a full-view raycast target over the top would eat every one of those.
        /// </summary>
        void BuildVignette(RectTransform view)
        {
            const int N = 128;
            _vignetteTex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "Map Vignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // how far out to the corner this pixel is, 0 in the middle and 1 at
                    // the corner itself, smoothed so there is no ring where it starts
                    float u = (x + 0.5f) / N * 2f - 1f;
                    float v = (y + 0.5f) / N * 2f - 1f;
                    float out2 = Mathf.Sqrt(u * u + v * v) / Mathf.Sqrt(2f);
                    float t = Mathf.InverseLerp(VignetteClear, 1f, out2);
                    t = t * t * (3f - 2f * t);
                    pixels[y * N + x] = new Color32(14, 14, 18, (byte)(t * 255f));
                }
            _vignetteTex.SetPixels32(pixels);
            _vignetteTex.Apply(false, false);

            _vignette = DemoUi.Block(view, "Vignette",
                new Color(1f, 1f, 1f, VignetteInk));
            _vignette.sprite = Sprite.Create(_vignetteTex,
                new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            _vignette.type = Image.Type.Simple;
            _vignette.raycastTarget = false;
            DemoUi.Fill(_vignette.rectTransform);
        }

        /// <summary>The building card: a printed slip on the plan, in the map's own
        /// paper rather than the demo's navy, printing the two lines the world's card
        /// prints.</summary>
        void BuildPopup(RectTransform view)
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - the map draws, but " +
                                 "its building card is off.");
                return;
            }

            _popupRect = DemoUi.NewRect("Card", view);
            _popup = _popupRect.gameObject;
            _popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            _popupRect.pivot = new Vector2(0.5f, 0f);

            var background = _popup.AddComponent<Image>();
            // A card you can click without clearing the selection under it.
            background.raycastTarget = true;
            DemoUi.Dress(background, DemoUi.Box, 15f, new Color(0.949f, 0.933f, 0.878f, 0.98f));

            var stripe = DemoUi.Block(_popupRect, "Accent", Picked);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(14f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -24f);

            _popupTitle = DemoUi.Text(_popupRect, "Title", 14f, Ink,
                TextAlignmentOptions.TopLeft, display: true);
            _popupTitle.characterSpacing = 2f;
            // Bake names run long ("building-apartment-01") and the card is a fixed
            // width - a clipped tail reads better than a name spilling off the box.
            _popupTitle.overflowMode = TextOverflowModes.Ellipsis;
            var titleRect = _popupTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(26f, -36f);
            titleRect.offsetMax = new Vector2(-14f, -10f);

            _popupBody = DemoUi.Text(_popupRect, "Body", 12.5f,
                new Color(0.259f, 0.243f, 0.220f), TextAlignmentOptions.TopLeft);
            _popupBody.textWrappingMode = TextWrappingModes.Normal;
            var bodyRect = _popupBody.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(26f, 10f);
            bodyRect.offsetMax = new Vector2(-14f, -40f);

            _popup.SetActive(false);
        }

        // ----------------------------------------------------------------- modes

        /// <summary>World XZ to view-local reference pixels: north up, east right.</summary>
        Vector2 ToView(Vector2 world) => (world - _centre) * _scale;

        void Update()
        {
            if (_panel == null)
                return;

            // The book owns the screen when it is open and the map is its other half;
            // otherwise the plan is up for exactly as long as the boom is past the
            // line the camera draws at 180 m.
            var want = LivingCity.UI.PersonnelAlmanac.IsOpen ? Mode.Docked
                : _rig != null && _rig.MapOut ? Mode.Full
                // and never a corner map over a map that has the whole screen
                : minimap && !LivingCity.UI.StrategicMapHud.IsOpen ? Mode.Corner
                : Mode.Off;
            if (want == _mode)
                return;

            // Going down into the street: the player lands on the place he had the
            // pointer over, not on whatever happened to be in the middle of the plan.
            if (want != Mode.Full && want != Mode.Docked && _mode == Mode.Full &&
                _scale > 0f && _rig != null && CursorOnMap(out var leaving))
            {
                var under = Under(leaving);
                _rig.pivot = new Vector3(under.x, _rig.pivot.y, under.y);
            }

            ApplyMode(want);
        }

        void ApplyMode(Mode mode)
        {
            // "The map is up" means the map has the SCREEN - the corner panel is a
            // thing on the HUD, not a thing instead of the city, so it neither sounds a
            // page turn nor takes the world's own building card away.
            bool was = _mode == Mode.Docked || _mode == Mode.Full;
            _mode = mode;
            bool on = mode == Mode.Docked || mode == Mode.Full;

            if (on != was)
            {
                DemoAudio.Ui(on ? DemoSounds.MapOpen : DemoSounds.MapClose);
                // The world picker draws an IMGUI card that would print straight over
                // the plan, and a card left open when the map came up would hang
                // there. It stands down for as long as the map is up.
                if (_picker)
                    _picker.enabled = !on;
                Select(-1);
            }

            // Coming up out of the street: the plan opens around the ground the
            // pointer was over, the same way the wheel works once it is up.
            if (mode == Mode.Full && !was && _cam != null && _rig != null &&
                Mouse.current != null)
            {
                var ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                var ground = new Plane(Vector3.up, new Vector3(0f, _rig.pivot.y, 0f));
                if (ground.Raycast(ray, out float along) && along > 0f && along < 4000f)
                {
                    var hit = ray.GetPoint(along);
                    _rig.pivot = new Vector3(hit.x, _rig.pivot.y, hit.z);
                }
            }

            _panel.SetActive(mode != Mode.Off);
            if (mode == Mode.Corner)
            {
                // A panel in the bottom right corner, the size of a postcard: the plan
                // held under the camera's own pivot so the player can see what is round
                // the corner from him without leaving the street.
                _canvas.sortingOrder = CornerOrder;
                _panelRect.anchorMin = new Vector2(1f, 0f);
                _panelRect.anchorMax = new Vector2(1f, 0f);
                _panelRect.pivot = new Vector2(1f, 0f);
                _panelRect.anchoredPosition = new Vector2(-CornerInset, CornerInset);
                _panelRect.sizeDelta = new Vector2(CornerWidth, CornerHeight);
                _panelFace.enabled = true;
                _view.offsetMin = new Vector2(6f, 6f);
                _view.offsetMax = new Vector2(-6f, -6f);
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
                _view.offsetMin = new Vector2(ViewPad, ViewPad);
                _view.offsetMax = new Vector2(-ViewPad, -(HeaderHeight + ViewPad * 0.5f));
            }
            else if (mode == Mode.Full)
            {
                // The whole screen, edge to edge, under the HUD.
                _canvas.sortingOrder = FullOrder;
                _panelRect.pivot = new Vector2(0.5f, 0.5f);
                _panelRect.anchorMin = Vector2.zero;
                _panelRect.anchorMax = Vector2.one;
                _panelRect.offsetMin = Vector2.zero;
                _panelRect.offsetMax = Vector2.zero;
                _panelFace.enabled = false;
                _view.offsetMin = Vector2.zero;
                _view.offsetMax = Vector2.zero;
            }

            if (_header) _header.SetActive(mode == Mode.Docked);
            if (_caption) _caption.gameObject.SetActive(mode == Mode.Full);

            // What the corner has no room for: the lettering (a street name across a
            // postcard covers the street it names) and the card, which is a thing read
            // on the big plan. The marks stay - the whole point of a corner map is
            // seeing the station and the family doors around you - only smaller.
            // Each mark is scaled about its own middle and never the root it hangs on -
            // the root is a full-screen rect whose children are PLACED in it, and
            // scaling that would move every mark off the ground it stands on.
            if (_names) _names.gameObject.SetActive(mode != Mode.Corner);
            var marks = MarkScale;
            for (int i = 0; i < _trades.Count; i++)
                _trades[i].Rect.localScale = marks;
            for (int i = 0; i < _frontDots.Count; i++)
                _frontDots[i].rectTransform.localScale = marks;

            // The crowd is not plotted onto a postcard: ten thousand civilians placed
            // every frame for a panel the size of a hand is the one thing here that
            // would cost real time, and a minimap wants the crews, the police and the
            // doors on it, not the crowd.
            if (mode == Mode.Corner)
                for (int i = 0; i < _movers.Count; i++)
                    if (_movers[i].Civilian && _movers[i].Img != null && _movers[i].Img.enabled)
                        _movers[i].Img.enabled = false;

            HoldCamera(mode == Mode.Full);
            _viewSize = Vector2.zero;   // the view changed shape: refit next frame
        }

        /// <summary>
        /// While the plan covers the screen the city behind it is drawn for nobody, so
        /// the camera is told to render nothing at all: the frame is cleared to the
        /// map's own sea and the pass costs a clear. Everything is put back exactly as
        /// it was when the player drops down into the street again.
        /// </summary>
        void HoldCamera(bool hold)
        {
            if (_cam == null)
                return;

            if (hold)
            {
                if (_cam.cullingMask == 0)
                    return;   // already held
                _camMask = _cam.cullingMask;
                _camClear = _cam.clearFlags;
                _camBackground = _cam.backgroundColor;
                _cam.cullingMask = 0;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Sea;
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

        // ---------------------------------------------------------------- layout

        /// <summary>What the view should be showing: the whole city while docked,
        /// whatever the camera's boom and pivot say while the map is the screen.</summary>
        void ViewWants(Vector2 size, out float scale, out Vector2 centre)
        {
            if (_mode == Mode.Full && _rig != null)
            {
                scale = size.y / Mathf.Max(40f, _rig.distance * BoomToMetres);
                centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            }
            else if (_mode == Mode.Corner && _rig != null)
            {
                // A fixed bite of ground under the camera's own pivot: the corner map
                // does not zoom with the wheel, or the one panel that is meant to be the
                // same every time you glance at it would never look the same twice.
                scale = size.y / CornerMetres;
                centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            }
            else
            {
                scale = Mathf.Min(size.x / _reach.width, size.y / _reach.height);
                centre = _reach.center;
            }
        }

        /// <summary>Where the pointer is on the plan, in the view's own reference
        /// pixels from its middle; false when it is not over the map at all.</summary>
        bool CursorOnMap(out Vector2 point)
        {
            point = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse == null || _view == null)
                return false;
            var screen = mouse.position.ReadValue();
            return RectTransformUtility.RectangleContainsScreenPoint(_view, screen, null) &&
                   RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       _view, screen, null, out point);
        }

        /// <summary>The ground under a point of the view, as the plan is drawn now.</summary>
        Vector2 Under(Vector2 point) => _centre + point / _scale;

        /// <summary>Hold the ground under the pointer still across a change of boom:
        /// the camera's pivot is moved so that what the wheel was aimed at is still
        /// under the mouse at the new zoom.</summary>
        void AnchorZoom(Vector2 point)
        {
            var under = Under(point);
            float grown = _view.rect.size.y / Mathf.Max(40f, _rig.distance * BoomToMetres);
            if (grown <= 0f)
                return;
            var centre = under - point / grown;
            _rig.pivot = new Vector3(centre.x, _rig.pivot.y, centre.y);
        }

        /// <summary>Re-fits the plan to the view. Zoom and pan are one scale and one
        /// offset on the content rect - there is no per-rect work in here.</summary>
        void Relayout()
        {
            var size = _view.rect.size;
            if (size.x <= 1f || size.y <= 1f)
                return;

            _viewSize = size;
            ViewWants(size, out _scale, out _centre);
            _laidDistance = _rig != null ? _rig.distance : 0f;

            _content.localScale = new Vector3(_scale, _scale, 1f);
            _content.anchoredPosition = (_origin - _centre) * _scale;

            // What ground the view is over, and which meshes therefore have to be drawn
            // at all. The corner panel looks at three hundred metres of a city a mile
            // and a half across; without this it would submit every roof in town, forty
            // times a second, to draw four blocks.
            if (_sheets.Count > 0)
            {
                float halfW = size.x * 0.5f / _scale + SheetPad;
                float halfH = size.y * 0.5f / _scale + SheetPad;
                float x0 = _centre.x - halfW, x1 = _centre.x + halfW;
                float z0 = _centre.y - halfH, z1 = _centre.y + halfH;
                for (int i = 0; i < _sheets.Count; i++)
                {
                    var sheet = _sheets[i];
                    bool on = _scale >= sheet.MinScale &&
                              sheet.Ground.xMax >= x0 && sheet.Ground.xMin <= x1 &&
                              sheet.Ground.yMax >= z0 && sheet.Ground.yMin <= z1;
                    if (on == sheet.On) continue;
                    sheet.On = on;
                    sheet.Go.SetActive(on);
                }
            }

            // How many printings of a street name are skipped between the ones kept:
            // the plan is pulled back, the repeats crowd, and every second or third one
            // is enough to name the street.
            int stride = Mathf.Max(1,
                Mathf.CeilToInt(NameGapPx / Mathf.Max(1f, LabelStep * _scale)));

            for (int i = 0; i < _labels.Count; i++)
            {
                var label = _labels[i];
                bool on;
                if (label.ScreenPx > 0f)
                {
                    // A street's name: held at its own size on screen, and printed only
                    // while its street has room on the plan to be named.
                    float grew = Mathf.Clamp(
                        label.ScreenPx / Mathf.Max(0.001f, label.Metres * _scale),
                        TypeGrowLow, TypeGrowHigh);
                    label.Rect.localScale = new Vector3(grew, grew, 1f);
                    on = label.Ordinal % stride == 0 && label.GapMetres * _scale >= NameLinePx;
                }
                else
                {
                    float px = label.Metres * _scale;
                    on = px >= label.MinPx && px <= label.MaxPx;
                }
                if (on == label.On)
                    continue;
                label.On = on;
                label.Rect.gameObject.SetActive(on);
            }

            // The crowd and the traffic at the size they really are, once the plan is
            // close enough for that to be bigger than the pip they are drawn as from a
            // mile up. Done on a CHANGE OF ZOOM only: panning does not change a mark's
            // size, and ten thousand rects resized every frame of a drag is the one
            // thing on this map that would cost real time.
            if (!Mathf.Approximately(_scale, _markedAt))
            {
                _markedAt = _scale;
                ResizeMarks();
            }

            // The plaques are drawn in the VIEW and not on the plan, so they keep their
            // size while the ground under them changes scale; they move when it does.
            for (int i = 0; i < _trades.Count; i++)
                _trades[i].Rect.anchoredPosition = ToView(_trades[i].At);

            if (_selected >= 0)
                PlaceCard(_buildings[_selected]);
        }

        // ------------------------------------------------------------ the running

        /// <summary>The zoom the marks were last sized for, so they are only resized
        /// when it changes.</summary>
        float _markedAt = -1f;

        /// <summary>How many pixels a thing this many metres across is drawn at, never
        /// below the floor the mark is worth seeing at.</summary>
        float MarkPx(float metres, float floor) =>
            Mathf.Max(floor, metres * _scale * (_mode == Mode.Corner ? CornerMarks : 1f));

        void ResizeMarks()
        {
            for (int i = 0; i < _movers.Count; i++)
            {
                var mover = _movers[i];
                if (mover.Img == null)
                    continue;
                mover.Img.rectTransform.sizeDelta = mover.Vehicle
                    ? new Vector2(MarkPx(CarBodyWide, mover.Floor.x),
                                  MarkPx(CarBodyLong, mover.Floor.y))
                    : new Vector2(MarkPx(ManWide, mover.Floor.x),
                                  MarkPx(ManWide, mover.Floor.y));
            }
        }

        void LateUpdate()
        {
            if (_mode == Mode.Off)
                return;

            // The wheel zooms the plan ABOUT THE POINTER: whatever street corner is
            // under the mouse stays under it, so zooming in walks the picture towards
            // the block the player is looking at instead of straight down the middle.
            // Worked out against the fit the last frame was drawn with, so it holds
            // whichever way round the camera and the map update this frame.
            if (_mode == Mode.Full && _rig != null && _scale > 0f &&
                !Mathf.Approximately(_rig.distance, _laidDistance) &&
                CursorOnMap(out var at))
                AnchorZoom(at);

            // The full-screen map rides the camera, so it re-fits whenever the wheel or
            // the keys have moved it - which is most frames the player is working.
            var size = _view.rect.size;
            ViewWants(size, out float scale, out Vector2 centre);
            if (size != _viewSize || !Mathf.Approximately(scale, _scale) || centre != _centre)
                Relayout();
            if (_scale <= 0f)
                return;

            bool corner = _mode == Mode.Corner;
            for (int i = 0; i < _movers.Count; i++)
            {
                var mover = _movers[i];
                if (corner && mover.Civilian)
                    continue;   // switched off when the corner panel came up
                var tf = mover.Tf;
                // Civilians step inside buildings and switch their object off; a
                // dot must not stand in the street while its owner is indoors.
                if (tf == null || !tf.gameObject.activeInHierarchy)
                {
                    if (mover.Img.enabled)
                        mover.Img.enabled = false;
                    continue;
                }

                var position = tf.position;
                var rect = mover.Img.rectTransform;
                rect.anchoredPosition = ToView(new Vector2(position.x, position.z));
                if (mover.Vehicle)
                    rect.localRotation = Quaternion.Euler(0f, 0f, -tf.eulerAngles.y);
                if (mover.Patrol != null)
                    mover.Img.color = mover.Patrol.MarkerDimmed ? PoliceRest : mover.Tint;
                if (!mover.Img.enabled)
                    mover.Img.enabled = true;
            }

            // Whose ground is whose: the families are seated after the map is built and
            // a front can burn down later, so the wash is asked about now and then and
            // re-laid only when the answer has changed.
            if (Time.unscaledTime >= _turfDue)
            {
                _turfDue = Time.unscaledTime + 0.75f;
                RefreshTurf();
            }

            PlotFrame();
            PlotCrews();
            PlotFronts();
        }

        // ------------------------------------------------------------- the fronts

        readonly List<Image> _frontDots = new List<Image>();
        readonly List<TMP_Text> _frontMarks = new List<TMP_Text>();

        /// <summary>Where each family keeps a door: a small SQUARE in its own colour, so
        /// premises never read as a man (the crews are round dots). Without these the
        /// twenty fronts are twenty buildings among a thousand and the player has no way
        /// to find the one he was told about - the card that opens on them is only worth
        /// having if the door can be found first.
        ///
        /// Positions never change, but the map re-lays itself on every zoom and pan, so
        /// the dots are placed each frame like every other marker here.</summary>
        void PlotFronts()
        {
            if (_moverRoot == null) return;

            var fronts = GangFront.All;
            while (_frontDots.Count < fronts.Count)
            {
                var plaque = DemoUi.Block(_moverRoot, "front", OutfitGold);
                DemoUi.Dress(plaque, DemoUi.Box, 8f, OutfitGold);
                plaque.raycastTarget = false;
                plaque.rectTransform.sizeDelta = new Vector2(FrontPx, FrontPx);
                plaque.rectTransform.localScale = MarkScale;
                _frontDots.Add(plaque);

                TMP_Text mark = null;
                if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
                {
                    mark = DemoUi.Text(plaque.rectTransform, "Mark", FrontPx * 0.66f,
                        Ink, TextAlignmentOptions.Center, display: true);
                    mark.raycastTarget = false;
                    DemoUi.Fill(mark.rectTransform);
                }
                _frontMarks.Add(mark);
            }

            for (int i = 0; i < _frontDots.Count; i++)
            {
                var live = i < fronts.Count && fronts[i] != null;
                if (_frontDots[i].enabled != live) _frontDots[i].enabled = live;
                if (_frontMarks[i] != null && _frontMarks[i].enabled != live)
                    _frontMarks[i].enabled = live;
                if (!live) continue;

                var front = fronts[i];
                var tint = LivingCity.UI.GangPalette.Of(front.GangId);
                // A burnt-out front is not a going concern: its plaque goes grey, which
                // is the one thing the player wants to see from the far end of the wheel
                // after a bomb has gone off.
                _frontDots[i].color = front.Boarded || front.Damaged ? Ashes : tint;
                _frontDots[i].rectTransform.anchoredPosition =
                    ToView(new Vector2(front.Door.x, front.Door.z));

                if (_frontMarks[i] == null) continue;
                _frontMarks[i].text = Initial(front.GangName);
                // Dark ink on a light colour, pale ink on a dark one - twenty families
                // and no reading the letter off half of them otherwise.
                _frontMarks[i].color = Bright(_frontDots[i].color) ? Ink : Paper;
            }
        }

        static readonly Color Ashes = new Color(0.43f, 0.42f, 0.40f, 1f);
        static readonly Color Paper = new Color(0.965f, 0.949f, 0.898f, 1f);

        /// <summary>The letter a family's door wears: the first letter of its name, past
        /// the articles a family name is apt to open with.</summary>
        static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var words = name.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Length == 0) continue;
                if (words.Length > 1 && (word.Equals("the", System.StringComparison.OrdinalIgnoreCase) ||
                                         word.Equals("la", System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                return word.Substring(0, 1).ToUpperInvariant();
            }
            return name.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>Perceived lightness, the cheap way - enough to choose an ink.</summary>
        static bool Bright(Color c) => c.r * 0.30f + c.g * 0.59f + c.b * 0.11f > 0.55f;

        // The outfit's men are dealt after the map is built and re-dealt whenever the
        // ledger changes, so their dots are matched to the live roll call every frame
        // rather than pooled once: a lieutenant's dot a shade larger, the selected
        // crew's dots lit white.
        readonly List<CrewWalker> _crewSeen = new List<CrewWalker>();

        Image _incidentDot;

        // Where the shooting is (or lately was): a red dot that pulses while it is on
        // and fades once it is quiet.
        void PlotIncident()
        {
            if (_moverRoot == null) return;
            bool on = StreetAlarm.IncidentOpen;
            if (!on) { if (_incidentDot && _incidentDot.enabled) _incidentDot.enabled = false; return; }
            if (_incidentDot == null)
            {
                _incidentDot = DemoUi.Block(_moverRoot, "incident", RivalRed);
                _incidentDot.sprite = DemoUi.Dot;
                _incidentDot.raycastTarget = false;
            }
            float quiet = StreetAlarm.QuietFor;
            float pulse = quiet < 5f
                ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 8f)
                : Mathf.Lerp(0.6f, 0.15f, quiet / StreetAlarm.IncidentGap);
            float size = quiet < 5f ? 22f : 16f;
            _incidentDot.rectTransform.sizeDelta = new Vector2(size, size);
            var p = StreetAlarm.Incident;
            _incidentDot.rectTransform.anchoredPosition = ToView(new Vector2(p.x, p.z));
            var c = RivalRed;
            c.a = pulse;
            _incidentDot.color = c;
            if (!_incidentDot.enabled) _incidentDot.enabled = true;
        }

        void PlotCrews()
        {
            if (_crews == null || _moverRoot == null)
                return;
            PlotIncident();

            _crewSeen.Clear();
            foreach (var unit in _crews.Units)
            {
                bool lit = _crews.Selected == unit;
                foreach (var man in unit.All())
                {
                    if (man.Tf == null || man.Dead)
                        continue;
                    _crewSeen.Add(man);
                    if (!_crewDots.TryGetValue(man, out var dot))
                    {
                        dot = DemoUi.Block(_moverRoot, "crew", OutfitGold);
                        dot.sprite = DemoUi.Dot;
                        _crewDots[man] = dot;
                    }
                    // a man's own shoulders once the plan is walked into, and never
                    // smaller than the pip he has to be findable as from a mile up
                    float size = MarkPx(man.IsLieutenant ? ManWide * 1.5f : ManWide,
                        man.IsLieutenant ? BossDot : CrewDot);
                    dot.rectTransform.sizeDelta = new Vector2(size, size);
                    var position = man.Tf.position;
                    dot.rectTransform.anchoredPosition = ToView(new Vector2(position.x, position.z));
                    dot.color = lit ? Color.white : unit.IsPolice ? PoliceBlue
                        : unit.Faction != 0 ? RivalInk(unit.Faction) : OutfitGold;
                    if (!dot.enabled)
                        dot.enabled = true;
                }
            }

            if (_crewDots.Count == _crewSeen.Count)
                return;
            var stale = new List<CrewWalker>();
            foreach (var kv in _crewDots)
                if (!_crewSeen.Contains(kv.Key))
                    stale.Add(kv.Key);
            foreach (var man in stale)
            {
                if (_crewDots[man])
                    Destroy(_crewDots[man].gameObject);
                _crewDots.Remove(man);
            }
        }

        // ------------------------------------------------------------ the picking

        /// <summary>A click target on the map: a building by index, or the ground at
        /// -1, which clears the selection the way bare street does in the world. It
        /// takes the DRAG as well - dragging anywhere on the plan pans it, and the
        /// building under the finger is not selected by a drag that happened to start
        /// on its roof.</summary>
        sealed class MapZone : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
            IPointerExitHandler, IBeginDragHandler, IDragHandler
        {
            public DemoMap map;
            public int index;

            public void OnPointerClick(PointerEventData eventData) => map.Click(index);

            public void OnPointerEnter(PointerEventData eventData) => map.Hover(index, true);

            public void OnPointerExit(PointerEventData eventData) => map.Hover(index, false);

            public void OnBeginDrag(PointerEventData eventData) => map.BeginPan();

            public void OnDrag(PointerEventData eventData) => map.Pan(eventData.delta);
        }

        bool _panned;

        void BeginPan() => _panned = false;

        /// <summary>Drag the plan: the CAMERA is what moves, so letting go and pushing
        /// the wheel in puts the player down where he dragged to.</summary>
        void Pan(Vector2 screenDelta)
        {
            if ((_mode != Mode.Full && _mode != Mode.Corner) || _rig == null || _scale <= 0f)
                return;
            if (screenDelta.sqrMagnitude > 4f)
                _panned = true;
            float perPixel = 1f / (Mathf.Max(0.01f, _canvas.scaleFactor) * _scale);
            _rig.PanBy(-screenDelta * perPixel);
        }

        void Click(int index)
        {
            if (_panned)     // that was a drag across the plan, not a pick
            {
                _panned = false;
                return;
            }

            // On the corner panel bare ground means "take me there", which is what a
            // minimap is for - but a building means the same as it does on the big plan:
            // the card, which is small enough to stand inside the panel. A building is
            // clickable wherever it is drawn.
            if (_mode == Mode.Corner && index < 0)
            {
                if (_rig != null && _scale > 0f && CursorOnMap(out var point))
                {
                    var ground = Under(point);
                    _rig.pivot = new Vector3(ground.x, _rig.pivot.y, ground.y);
                }
                Select(-1);
                return;
            }

            Select(index);
        }

        void Hover(int index, bool over)
        {
            if (index < 0 || index == _selected || _mode == Mode.Corner)
                return;

            var building = _buildings[index];
            if (building.Face)
                building.Face.color = over
                    ? (building.Covered ? HoverWash : Color.white)
                    : Idle(building);
        }

        void Select(int index)
        {
            if (_selected >= 0 && _selected < _buildings.Count)
            {
                var previous = _buildings[_selected];
                if (previous.Face)
                    previous.Face.color = Idle(previous);
            }

            _selected = index;
            if (_popup == null)
                return;

            if (index < 0)
            {
                _popup.SetActive(false);
                return;
            }

            var building = _buildings[index];
            if (building.Face)
                building.Face.color = building.Covered ? PickWash : Picked;
            _popupTitle.text = building.Title;
            _popupBody.text = building.Body;
            _popup.SetActive(true);
            PlaceCard(building);
        }

        /// <summary>The card sits over its building and stays inside the view.</summary>
        void PlaceCard(Building building)
        {
            if (_popup == null || _scale <= 0f)
                return;

            var top = ToView(new Vector2(building.World.center.x, building.World.yMax));
            var half = _viewSize * 0.5f;
            _popupRect.anchoredPosition = new Vector2(
                Mathf.Clamp(top.x, -half.x + PopupWidth * 0.5f, half.x - PopupWidth * 0.5f),
                Mathf.Clamp(top.y + PopupLift, -half.y, half.y - PopupHeight));
        }

        void OnDestroy()
        {
            if (_vignetteTex != null)
                Destroy(_vignetteTex);

            // Play-stop with the map up must give the world its picker and its camera
            // back - both live on objects that outlive this one in the editor.
            if (_mode != Mode.Off && _picker)
                _picker.enabled = true;
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
            HoldCamera(false);
        }
    }
}
