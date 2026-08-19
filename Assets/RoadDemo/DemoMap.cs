using System.Collections.Generic;
using LivingCity.CameraRig;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The demo's top-down city map: the war-room half of the screen. The ledger the
    /// demo installs takes the LEFT half when it opens and leaves the right one empty,
    /// so the map moves in there for exactly as long as the book stands open, and
    /// leaves with it.
    ///
    /// The map DRAWS the plan rather than photographing it - no second camera, no
    /// render texture. The block slabs come off the same grid arithmetic the kit is
    /// laid on (RoadDemoBuilder.LotPlans and the two half-widths), the buildings off the
    /// very colliders BuildingCardPicker raycasts in the world, so what is clickable
    /// on the map is exactly what is clickable in the scene - and the card it opens
    /// prints the same two lines the world card does.
    ///
    /// Everybody moving is a marker over that plan, on the police overlay's colour
    /// convention: dots for people (white civilians, blue police), little rectangles
    /// turned to their heading for vehicles. Markers are pooled once at build - one
    /// Image per subject, position set per frame - which is the crowd this demo runs
    /// (some three hundred) at a couple of hundred rect writes a frame.
    ///
    /// Demo-local like every other screen in this folder: it dresses from DemoUi (the
    /// Interface Modern Menus pack) and borrows no part of LivingCity's own strategic
    /// map, which is built around a CityBuilder this scene does not have.
    /// </summary>
    public class DemoMap : MonoBehaviour
    {
        const int SortingOrder = 30;   // over the top bar's 20, clear of the book's 110

        // The panel's inset inside the right half. The top clears the demo's top bar,
        // which retracts into this same half while the book is open.
        const float PanelLeft = 14f, PanelRight = 22f, PanelTop = 66f, PanelBottom = 22f;
        const float HeaderHeight = 40f;
        const float ViewPad = 12f;
        const float WorldMargin = 6f;  // metres of air around the city, so nothing touches the frame

        const float PedDot = 6f;
        const float PoliceDot = 8f;
        const float CrewDot = 8f;
        const float BossDot = 10f;
        const float CarLength = 9f, CarWidth = 5f;

        /// <summary>A footprint never draws thinner than this - a box has to hold a
        /// drawn rim and still be worth aiming at.</summary>
        const float MinBuilding = 7f;

        /// <summary>Reference pixels of drawn rim: the hairline every clickable
        /// footprint wears, and the heavier line the picked one gets.</summary>
        const float Rim = 1f, PickedRim = 2f;

        const float PopupWidth = 250f, PopupHeight = 88f, PopupLift = 10f;

        // ------------------------------------------------------------------ colours
        //
        // One family with the rest of the demo's screens: DemoUi's ink and accent on a
        // ground darker than any panel, so the map reads as a surface being looked at
        // rather than a page being read.

        static readonly Color Outside = new Color(0.020f, 0.036f, 0.055f, 1f);
        static readonly Color Asphalt = new Color(0.055f, 0.085f, 0.120f, 1f);
        static readonly Color Avenue = new Color(0.082f, 0.125f, 0.170f, 1f);
        static readonly Color Median = new Color(0.42f, 0.35f, 0.16f, 0.85f);
        static readonly Color BlockFace = new Color(0.100f, 0.170f, 0.230f, 1f);
        // the seams: the river a shade of the navy that reads as water beside the
        // slabs, the park a muted green in the same key
        static readonly Color River = new Color(0.075f, 0.200f, 0.330f, 1f);
        static readonly Color Lawn = new Color(0.090f, 0.200f, 0.150f, 1f);
        static readonly Color Deck = new Color(0.150f, 0.190f, 0.230f, 1f);

        /// <summary>A footprint's own outline. Buildings are drawn as rims and not as
        /// slabs on purpose: an outline says "this box takes a click" without painting
        /// over the block it stands on, and a packed row still reads as many buildings
        /// rather than one lit mass.</summary>
        static readonly Color BuildingRim = new Color(0.46f, 0.63f, 0.74f, 0.9f);

        /// <summary>The patrol overlay's own duty blue, and its resting fade.</summary>
        static readonly Color PoliceBlue = new Color(0.38f, 0.70f, 1f, 1f);
        static readonly Color PoliceRest = new Color(0.38f, 0.70f, 1f, 0.35f);
        static readonly Color TrafficInk = new Color(0.78f, 0.86f, 0.96f, 0.92f);

        // ------------------------------------------------------------------- wiring

        RoadDemoBuilder _builder;
        Transform _blockRoot;
        BuildingCardPicker _picker;
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

        readonly List<Building> _buildings = new List<Building>();
        readonly List<Mover> _movers = new List<Mover>();

        // Static art that has to be re-placed whenever the view changes size: the
        // slabs and bands, each remembered with the world rect it stands for.
        readonly List<(RectTransform rect, Rect world)> _plan =
            new List<(RectTransform, Rect)>();

        GameObject _panel;
        RectTransform _view;
        Canvas _canvas;
        GameObject _popup;
        RectTransform _popupRect;
        TMP_Text _popupTitle, _popupBody;

        Rect _world;          // the city's own extent, metres
        float _scale;         // reference pixels per metre
        Vector2 _viewSize;    // the view rect the current scale was computed for
        int _selected = -1;
        bool _shown;

        public void Init(RoadDemoBuilder builder, Transform blockRoot,
            BuildingCardPicker picker, List<CivilianAgent> civilians,
            List<PoliceFootPatrol> officers, List<DemoVehicle> cars,
            List<PolicePatrolCar> policeCars, DemoCrews crews)
        {
            _builder = builder;
            _blockRoot = blockRoot;
            _picker = picker;
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
        }

        // --------------------------------------------------------------- the plan

        void MeasureWorld()
        {
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            _world = Rect.MinMaxRect(
                vx[0] - _builder.VerticalHalfWidth(0) - WorldMargin,
                hz[0] - _builder.HorizontalHalfWidth(0) - WorldMargin,
                vx[vx.Length - 1] + _builder.VerticalHalfWidth(vx.Length - 1) + WorldMargin,
                hz[hz.Length - 1] + _builder.HorizontalHalfWidth(hz.Length - 1) + WorldMargin);
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

        // ------------------------------------------------------------ construction

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

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

            // The right half of the screen, the half the book leaves empty. Its own
            // face is a raycast target on purpose: that is what stops a click landing
            // on the city behind it.
            var panel = DemoUi.NewRect("Map Panel", transform);
            _panel = panel.gameObject;
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(PanelLeft, PanelBottom);
            panel.offsetMax = new Vector2(-PanelRight, -PanelTop);

            var face = panel.gameObject.AddComponent<Image>();
            face.raycastTarget = true;
            DemoUi.Dress(face, DemoUi.Box, 15f, DemoUi.Panel);

            BuildHeader(panel);

            var view = DemoUi.NewRect("View", panel);
            _view = view;
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(ViewPad, ViewPad);
            view.offsetMax = new Vector2(-ViewPad, -(HeaderHeight + ViewPad * 0.5f));
            view.gameObject.AddComponent<RectMask2D>();

            // Out of town: the ground under the whole plot, and the click that clears
            // a selection - the same gesture as clicking bare street in the world.
            var ground = DemoUi.Block(view, "Ground", Outside);
            DemoUi.Fill(ground.rectTransform);
            ground.raycastTarget = true;
            var clear = ground.gameObject.AddComponent<MapZone>();
            clear.map = this;
            clear.index = -1;

            BuildPlan(view);
            BuildMovers(view);
            // Last child of the view, so it prints over the plan and the crowd, and
            // shares the view's own coordinates - the card is placed off a footprint.
            BuildPopup(view);

            // Built ACTIVE for TMP's sake (a text only loads its font in OnEnable, which
            // never runs under an inactive parent), then hidden until the book opens.
            _panel.SetActive(false);
        }

        void BuildHeader(RectTransform panel)
        {
            var title = DemoUi.Text(panel, "Title", 15f, DemoUi.Ink,
                TextAlignmentOptions.MidlineLeft, display: true);
            title.characterSpacing = 3f;
            title.text = "CITY MAP";
            var rect = title.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(ViewPad + 2f, -ViewPad);
            rect.sizeDelta = new Vector2(-ViewPad, HeaderHeight - ViewPad);

            // The key, right-shouldered on the same line: what a dot is, what a
            // rectangle is, and what blue means.
            var legend = DemoUi.NewRect("Legend", panel);
            legend.anchorMin = new Vector2(1f, 1f);
            legend.anchorMax = new Vector2(1f, 1f);
            legend.pivot = new Vector2(1f, 1f);
            legend.anchoredPosition = new Vector2(-ViewPad, -ViewPad);
            legend.sizeDelta = new Vector2(4f * LegendStep, HeaderHeight - ViewPad);

            LegendChip(legend, 0, DemoUi.Dot, new Vector2(9f, 9f), PoliceBlue, "POLICE");
            LegendChip(legend, 1, DemoUi.Dot, new Vector2(9f, 9f), DemoUi.Ink, "CIVILIAN");
            LegendChip(legend, 2, null, new Vector2(5f, 9f), TrafficInk, "TRAFFIC");
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

        /// <summary>The drawn city, bottom layer up: the town's own ground, the wide
        /// roads that read as avenues, the block slabs kerb to kerb, and the buildings
        /// standing on them. Positions are filled in by Relayout, which is the only
        /// place that knows how many pixels a metre is worth.</summary>
        void BuildPlan(RectTransform view)
        {
            var town = DemoUi.Block(view, "Town", Asphalt);
            _plan.Add((town.rectTransform, _world));

            // the seams under the roads: the water the bridges cross, the park's lawn
            var seams = DemoUi.NewRect("Seams", view);
            DemoUi.Fill(seams);
            foreach (var seam in _builder.SeamPlans)
                _plan.Add((DemoUi.Block(seams, seam.Kind.ToString(),
                    seam.Kind == SeamKind.River ? River : seam.Kind == SeamKind.Highway ? Deck : Lawn).rectTransform,
                    seam.Area));

            var roads = DemoUi.NewRect("Roads", view);
            DemoUi.Fill(roads);
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            for (int i = 0; i < vx.Length; i++)
            {
                if (!_builder.verticalIsBoulevard[i])
                    continue;
                float half = _builder.VerticalHalfWidth(i);
                _plan.Add((DemoUi.Block(roads, "Avenue", Avenue).rectTransform,
                    Rect.MinMaxRect(vx[i] - half, _world.yMin, vx[i] + half, _world.yMax)));
                _plan.Add((DemoUi.Block(roads, "Median", Median).rectTransform,
                    Rect.MinMaxRect(vx[i] - 0.7f, _world.yMin, vx[i] + 0.7f, _world.yMax)));
            }
            for (int j = 0; j < hz.Length; j++)
            {
                if (!_builder.horizontalIsBoulevard[j])
                    continue;
                float half = _builder.HorizontalHalfWidth(j);
                _plan.Add((DemoUi.Block(roads, "Avenue", Avenue).rectTransform,
                    Rect.MinMaxRect(_world.xMin, hz[j] - half, _world.xMax, hz[j] + half)));
                _plan.Add((DemoUi.Block(roads, "Median", Median).rectTransform,
                    Rect.MinMaxRect(_world.xMin, hz[j] - 0.7f, _world.xMax, hz[j] + 0.7f)));
            }

            var blocks = DemoUi.NewRect("Blocks", view);
            DemoUi.Fill(blocks);
            foreach (var lot in _builder.LotPlans)
                _plan.Add((DemoUi.Block(blocks, "Block", BlockFace).rectTransform, lot.Slab));

            var buildings = DemoUi.NewRect("Buildings", view);
            DemoUi.Fill(buildings);
            for (int i = 0; i < _buildings.Count; i++)
            {
                var building = _buildings[i];
                var face = DemoUi.Block(buildings, building.Title, BuildingRim);
                face.sprite = OutlineSprite();
                face.type = Image.Type.Sliced;
                // Rim only - the centre is never drawn. The CLICK still takes the whole
                // rect: a Graphic answers the pointer by its rect, not by what it drew.
                face.fillCenter = false;
                face.raycastTarget = true;
                building.Face = face;
                SetRim(face, Rim);

                var zone = face.gameObject.AddComponent<MapZone>();
                zone.map = this;
                zone.index = i;
            }
        }

        Sprite _outline;

        /// <summary>
        /// A hairline frame at any size: three pixels square, sliced one pixel in on
        /// every side, so the corners stay one pixel and the four edges stretch. The
        /// sprite is authored at the canvas's own 100 reference pixels per unit, which
        /// makes the drawn rim exactly 1 / pixelsPerUnitMultiplier reference pixels -
        /// see SetRim. Built here rather than pulled from the pack: the Modern Menus
        /// frames carry thick decorated borders that would swallow a 20-pixel box.
        /// </summary>
        Sprite OutlineSprite()
        {
            if (_outline)
                return _outline;

            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                name = "DemoMap Outline",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[9];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            _outline = Sprite.Create(texture, new Rect(0f, 0f, 3f, 3f),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(1f, 1f, 1f, 1f));
            _outline.hideFlags = HideFlags.HideAndDontSave;
            return _outline;
        }

        /// <summary>How thick the rim draws, in reference pixels.</summary>
        static void SetRim(Image image, float thickness) =>
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(thickness, 0.01f);

        void BuildMovers(RectTransform view)
        {
            var root = _moverRoot = DemoUi.NewRect("Movers", view);
            DemoUi.Fill(root);

            if (_civilians != null)
                foreach (var civilian in _civilians)
                    AddMover(root, civilian.Tf, null, false, DemoUi.Ink, PedDot, PedDot,
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

        /// <summary>The building card, in the demo's own wardrobe - the police popup's
        /// box and stripe, printing the two lines the world's card prints.</summary>
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
            DemoUi.Dress(background, DemoUi.Box, 15f, DemoUi.Panel);

            var stripe = DemoUi.Block(_popupRect, "Accent", DemoUi.Gold);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(14f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -24f);

            _popupTitle = DemoUi.Text(_popupRect, "Title", 14f, DemoUi.Ink,
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

            _popupBody = DemoUi.Text(_popupRect, "Body", 12.5f, DemoUi.InkDim,
                TextAlignmentOptions.TopLeft);
            _popupBody.textWrappingMode = TextWrappingModes.Normal;
            var bodyRect = _popupBody.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(26f, 10f);
            bodyRect.offsetMax = new Vector2(-14f, -40f);

            _popup.SetActive(false);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>World XZ to view-local reference pixels: north up, east right, one
        /// scale for both axes so a block keeps its proportions.</summary>
        Vector2 ToView(Vector2 world) => new Vector2(
            (world.x - _world.center.x) * _scale,
            (world.y - _world.center.y) * _scale);

        /// <summary>Places a world rect on the view. The floor is for footprints only -
        /// a median line is meant to come out hairline thin.</summary>
        void Place(RectTransform rect, Rect world, float floor = 0f)
        {
            rect.anchoredPosition = ToView(world.center);
            rect.sizeDelta = new Vector2(
                Mathf.Max(world.width * _scale, floor),
                Mathf.Max(world.height * _scale, floor));
        }

        /// <summary>Re-fits the whole plan to the view. Runs on the first show and
        /// again whenever the window changes shape - the docked half is a fraction of
        /// the screen, so its reference size is not a constant.</summary>
        void Relayout()
        {
            var size = _view.rect.size;
            if (size.x <= 1f || size.y <= 1f)
                return;

            _viewSize = size;
            _scale = Mathf.Min(size.x / _world.width, size.y / _world.height);

            foreach (var (rect, world) in _plan)
                Place(rect, world);
            foreach (var building in _buildings)
                if (building.Face)
                    Place(building.Face.rectTransform, building.World, MinBuilding);

            if (_selected >= 0)
                PlaceCard(_buildings[_selected]);
        }

        // ------------------------------------------------------------- the running

        void Update()
        {
            if (_panel == null)
                return;

            // The map is the book's other half: it comes up with it and goes down with
            // it, and nothing else opens it.
            var open = LivingCity.UI.PersonnelAlmanac.IsOpen;
            if (open == _shown)
                return;

            _shown = open;
            _panel.SetActive(open);
            DemoAudio.Ui(open ? DemoSounds.MapOpen : DemoSounds.MapClose);
            if (!open)
                Select(-1);

            // The world picker draws an IMGUI card that would print straight over the
            // book, and a card left open when the book opened would hang there. It
            // stands down for as long as the book is up; disabling closes it.
            if (_picker)
                _picker.enabled = !open;
        }

        void LateUpdate()
        {
            if (!_shown)
                return;

            if (_view.rect.size != _viewSize)
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

        // The outfit's men are dealt after the map is built and re-dealt whenever
        // the ledger changes, so their dots are matched to the live roll call every
        // frame rather than pooled once: a lieutenant's dot a shade larger, the
        // selected crew's dots lit white.
        readonly List<CrewWalker> _crewSeen = new List<CrewWalker>();
        static readonly Color RivalRed = new Color(1f, 0.36f, 0.30f, 1f);

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
                        dot = DemoUi.Block(_moverRoot, "crew", DemoUi.Gold);
                        dot.sprite = DemoUi.Dot;
                        _crewDots[man] = dot;
                    }
                    float size = man.IsLieutenant ? BossDot : CrewDot;
                    dot.rectTransform.sizeDelta = new Vector2(size, size);
                    var position = man.Tf.position;
                    dot.rectTransform.anchoredPosition = ToView(new Vector2(position.x, position.z));
                    dot.color = lit ? Color.white : unit.IsPolice ? PoliceBlue : unit.Faction != 0 ? RivalRed : DemoUi.Gold;
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

        // ------------------------------------------------------------- the picking

        /// <summary>A click target on the map: a building by index, or the ground at
        /// -1, which clears the selection the way bare street does in the world.</summary>
        sealed class MapZone : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
            IPointerExitHandler
        {
            public DemoMap map;
            public int index;

            public void OnPointerClick(PointerEventData eventData) => map.Select(index);

            public void OnPointerEnter(PointerEventData eventData) => map.Hover(index, true);

            public void OnPointerExit(PointerEventData eventData) => map.Hover(index, false);
        }

        void Hover(int index, bool over)
        {
            if (index < 0 || index == _selected)
                return;

            var face = _buildings[index].Face;
            if (face)
                face.color = over ? DemoUi.Accent : BuildingRim;
        }

        void Select(int index)
        {
            if (_selected >= 0 && _selected < _buildings.Count)
            {
                var previous = _buildings[_selected].Face;
                if (previous)
                {
                    previous.color = BuildingRim;
                    SetRim(previous, Rim);
                }
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
            {
                // The picked footprint answers in the gold the world card highlights
                // its building with, and on a heavier line so it holds in a packed row.
                building.Face.color = DemoUi.Gold;
                SetRim(building.Face, PickedRim);
            }
            _popupTitle.text = building.Title;
            _popupBody.text = building.Body;
            _popup.SetActive(true);
            PlaceCard(building);
        }

        /// <summary>The card sits over its building and stays inside the panel - the
        /// map has no room to let a card hang off its edge.</summary>
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
            // Play-stop with the book open must give the world its picker back.
            if (_shown && _picker)
                _picker.enabled = true;
        }
    }
}
