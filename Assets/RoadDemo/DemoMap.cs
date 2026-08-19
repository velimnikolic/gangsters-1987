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
    /// It comes up two ways, and it is the same map both times:
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

        // ----------------------------------------------------------------- glyphs

        const float PedDot = 5f;
        const float PoliceDot = 7f;
        const float CrewDot = 8f;
        const float BossDot = 11f;
        const float CarLength = 9f, CarWidth = 5f;

        /// <summary>No footprint is drawn thinner than this, or a shed is nothing at
        /// all. Metres, because the plan is laid out in metres.</summary>
        const float MinBuilding = 3f;

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

        // ------------------------------------------------------------------ paper

        // The plan's colours: paper, not the navy the rest of the demo's screens are
        // dressed in. The map is the one screen that is a PICTURE of the city, and the
        // original's is a bright printed sheet - grey streets, pale blocks, flat green
        // country, a blue river.
        static readonly Color Sea = new Color(0.176f, 0.353f, 0.541f, 1f);
        static readonly Color Shore = new Color(0.847f, 0.788f, 0.596f, 1f);
        static readonly Color Field = new Color(0.298f, 0.549f, 0.259f, 1f);
        static readonly Color FieldAlt = new Color(0.243f, 0.478f, 0.216f, 1f);
        static readonly Color Asphalt = new Color(0.482f, 0.478f, 0.459f, 1f);
        static readonly Color Avenue = new Color(0.545f, 0.541f, 0.518f, 1f);
        static readonly Color Median = new Color(0.925f, 0.882f, 0.643f, 1f);
        static readonly Color Slab = new Color(0.765f, 0.749f, 0.706f, 1f);    // the sidewalk ring
        static readonly Color LotFace = new Color(0.686f, 0.671f, 0.627f, 1f); // the yard inside it
        static readonly Color River = new Color(0.294f, 0.404f, 0.741f, 1f);
        static readonly Color Lawn = new Color(0.373f, 0.639f, 0.286f, 1f);
        static readonly Color Deck = new Color(0.388f, 0.384f, 0.376f, 1f);

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

        static readonly Color Ink = new Color(0.086f, 0.078f, 0.071f, 1f);
        static readonly Color Halo = new Color(1f, 0.98f, 0.92f, 0.55f);
        static readonly Color Picked = new Color(0.95f, 0.30f, 0.15f, 1f);

        static readonly Color PoliceBlue = new Color(0.10f, 0.32f, 0.85f, 1f);
        static readonly Color PoliceRest = new Color(0.10f, 0.32f, 0.85f, 0.35f);
        static readonly Color CivilianInk = new Color(0.12f, 0.12f, 0.13f, 0.85f);
        static readonly Color TrafficInk = new Color(0.20f, 0.22f, 0.26f, 0.95f);
        static readonly Color OutfitGold = new Color(0.94f, 0.72f, 0.13f, 1f);
        static readonly Color RivalRed = new Color(0.86f, 0.17f, 0.13f, 1f);

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
        }

        /// <summary>Anything that moves: one Image, positioned from a transform.</summary>
        struct Mover
        {
            public Transform Tf;
            public Image Img;
            public IPatrolMarker Patrol;  // null for civilians - only police rest
            public bool Vehicle;
            public Color Tint;
        }

        /// <summary>A name printed on the plan: how tall it is in metres of ground,
        /// and the window of screen sizes it is worth reading at.</summary>
        sealed class Label
        {
            public RectTransform Rect;
            public float Metres;
            public float MinPx, MaxPx;
            public bool On = true;
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
        Canvas _canvas;
        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupBody;
        TMP_Text _caption;

        Rect _world;          // the city's own extent, metres
        Vector2 _origin;      // the metre the content rect is measured from
        Vector2 _centre;      // the ground under the middle of the view
        float _scale;         // reference pixels per metre
        Vector2 _viewSize;    // the view rect the current fit was computed for
        float _laidDistance;  // the boom that fit was drawn for - what the wheel is measured against
        int _selected = -1;

        enum Mode { Off, Docked, Full }
        Mode _mode = Mode.Off;

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
            Build();

            // How far back the wheel may go: the last click is the whole island in the
            // frame. The camera does not know how big its island is; the map does.
            if (_rig != null)
            {
                var island = _builder.IslandArea;
                float span = island.height > 1f ? island.height : _world.height * 2.4f;
                _rig.mapCeiling = Mathf.Clamp(span / BoomToMetres, 400f, 6000f);
            }
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

            BuildLand(_content);
            BuildPlan(_content);
            BuildLabels(_content);
            BuildMovers(view);
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

            var host = DemoUi.NewRect("Country", content);
            host.anchoredPosition = area.center - _origin;
            host.sizeDelta = area.size;
            // The renderer BEFORE the graphic: RequireComponent is not inherited
            // through a Graphic subclass on every path, and a MaskableGraphic without
            // one throws inside RectMask2D's clipping - which aborts the whole
            // canvas update, and takes the entire map down with it.
            host.gameObject.AddComponent<CanvasRenderer>();
            var patches = host.gameObject.AddComponent<MapPatches>();
            patches.color = Color.white;

            // the cell grows with the island: at twenty metres a six-kilometre island is
            // ninety thousand samples and far more merged runs than one UI mesh can
            // hold, and the far half of the country simply stopped being drawn
            float Cell = Mathf.Max(20f, Mathf.Sqrt(area.width * area.height) / 200f);
            const float BeachLine = -0.35f;   // the line BuildGround itself wears sand at
            const float GrassLine = 0.55f;
            const int Budget = 12000;         // quads: one UI mesh may not pass 65k verts

            int nx = Mathf.CeilToInt(area.width / Cell);
            int nz = Mathf.CeilToInt(area.height / Cell);
            int runs = 0;

            for (int j = 0; j < nz && runs < Budget; j++)
            {
                float z = area.yMin + j * Cell;
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
                            here = h < BeachLine ? 0 : h < GrassLine ? 1 : Field2(x, cz);
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
            foreach (var lot in _builder.LotPlans)
            {
                Slot(blocks, "Block", lot.Slab, Slab);
                Slot(blocks, "Yard", lot.Interior, LotFace);
            }

            // the quarters, washed in their own colour over the ground they stand on,
            // and their houses over the wash - a quarter's buildings are not under
            // the Blocks root, so they come off the footprints it reported instead
            // and are drawn but not clickable
            var washes = DemoUi.NewRect("Quarters", content);
            foreach (var district in _builder.DistrictPlans)
                Slot(washes, district.Name, district.World,
                    district.Kind == DistrictKind.Harbor ? HarborWash :
                    district.Kind == DistrictKind.Suburb ? SuburbWash : PadWash);
            foreach (var roof in _builder.QuarterRoofs)
                Slot(washes, "Roof", roof, Roofs[RoofHash(roof.center) % 4], MinBuilding);

            var buildings = DemoUi.NewRect("Buildings", content);
            for (int i = 0; i < _buildings.Count; i++)
            {
                var building = _buildings[i];
                var face = Slot(buildings, building.Title, building.World, building.Roof,
                    MinBuilding);
                face.raycastTarget = true;
                building.Face = face;

                var zone = face.gameObject.AddComponent<MapZone>();
                zone.map = this;
                zone.index = i;
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
            var root = DemoUi.NewRect("Names", content);

            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            for (int i = 0; i < vx.Length; i++)
            {
                string name = names.Vertical(i);
                if (string.IsNullOrEmpty(name)) continue;
                bool grand = _builder.verticalIsBoulevard[i];
                float type = grand ? AvenueType : StreetType;
                for (float z = _world.yMin + LabelStep * 0.5f; z < _world.yMax; z += LabelStep)
                    Letter(root, name, new Vector2(vx[i], z), 90f, type,
                        grand ? MinTypePx * 0.75f : MinTypePx);
            }
            for (int j = 0; j < hz.Length; j++)
            {
                string name = names.Horizontal(j);
                if (string.IsNullOrEmpty(name)) continue;
                bool grand = _builder.horizontalIsBoulevard[j];
                float type = grand ? AvenueType : StreetType;
                for (float x = _world.xMin + LabelStep * 0.5f; x < _world.xMax; x += LabelStep)
                    Letter(root, name, new Vector2(x, hz[j]), 0f, type,
                        grand ? MinTypePx * 0.75f : MinTypePx);
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
            }

            // The town's own name across the middle of the grid, the way the original
            // prints its city across its map - and taken off again once the map is
            // close enough for the streets to carry their own names.
            Letter(root, names.City, _world.center, 0f, CityType, 6f, 70f);
        }

        /// <summary>One name on the plan: the ink, and a pale copy behind it so black
        /// letters still read over a dark green field or over the river.</summary>
        void Letter(Transform parent, string text, Vector2 at, float turn, float metres,
            float minPx, float maxPx = MaxTypePx)
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

            _labels.Add(new Label { Rect = host, Metres = metres, MinPx = minPx, MaxPx = maxPx });
        }

        // ------------------------------------------------------------- the crowd

        void BuildMovers(RectTransform view)
        {
            var root = _moverRoot = DemoUi.NewRect("Movers", view);
            DemoUi.Fill(root);

            if (_civilians != null)
                foreach (var civilian in _civilians)
                    AddMover(root, civilian.Tf, null, false, CivilianInk, PedDot, PedDot,
                        DemoUi.Dot);
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
            Color tint, float width, float height, Sprite sprite)
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
            });
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
                : Mode.Off;
            if (want == _mode)
                return;

            // Going down into the street: the player lands on the place he had the
            // pointer over, not on whatever happened to be in the middle of the plan.
            if (want == Mode.Off && _mode == Mode.Full && _scale > 0f && _rig != null &&
                CursorOnMap(out var leaving))
            {
                var under = Under(leaving);
                _rig.pivot = new Vector3(under.x, _rig.pivot.y, under.y);
            }

            ApplyMode(want);
        }

        void ApplyMode(Mode mode)
        {
            bool was = _mode != Mode.Off;
            _mode = mode;
            bool on = mode != Mode.Off;

            if (on != was)
            {
                DemoAudio.Ui(on ? DemoSounds.MapOpen : DemoSounds.MapClose);
                // The world picker draws an IMGUI card that would print straight over
                // the plan, and a card left open when the map came up would hang
                // there. It stands down for as long as the map is up.
                if (_picker)
                    _picker.enabled = !on;
                if (!on)
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

            _panel.SetActive(on);
            if (mode == Mode.Docked)
            {
                _canvas.sortingOrder = DockedOrder;
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
            else
            {
                scale = Mathf.Min(size.x / _world.width, size.y / _world.height);
                centre = _world.center;
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

            for (int i = 0; i < _labels.Count; i++)
            {
                var label = _labels[i];
                float px = label.Metres * _scale;
                bool on = px >= label.MinPx && px <= label.MaxPx;
                if (on == label.On)
                    continue;
                label.On = on;
                label.Rect.gameObject.SetActive(on);
            }

            if (_selected >= 0)
                PlaceCard(_buildings[_selected]);
        }

        // ------------------------------------------------------------ the running

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

            for (int i = 0; i < _movers.Count; i++)
            {
                var mover = _movers[i];
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

            PlotCrews();
        }

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
                    float size = man.IsLieutenant ? BossDot : CrewDot;
                    dot.rectTransform.sizeDelta = new Vector2(size, size);
                    var position = man.Tf.position;
                    dot.rectTransform.anchoredPosition = ToView(new Vector2(position.x, position.z));
                    dot.color = lit ? Color.white : unit.IsPolice ? PoliceBlue
                        : unit.Faction != 0 ? RivalRed : OutfitGold;
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
            if (_mode != Mode.Full || _rig == null || _scale <= 0f)
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
            Select(index);
        }

        void Hover(int index, bool over)
        {
            if (index < 0 || index == _selected)
                return;

            var face = _buildings[index].Face;
            if (face)
                face.color = over ? Color.white : _buildings[index].Roof;
        }

        void Select(int index)
        {
            if (_selected >= 0 && _selected < _buildings.Count)
            {
                var previous = _buildings[_selected];
                if (previous.Face)
                    previous.Face.color = previous.Roof;
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
                building.Face.color = Picked;
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
            // Play-stop with the map up must give the world its picker and its camera
            // back - both live on objects that outlive this one in the editor.
            if (_mode != Mode.Off && _picker)
                _picker.enabled = true;
            HoldCamera(false);
        }
    }
}
