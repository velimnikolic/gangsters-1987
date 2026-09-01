using System.Collections.Generic;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// Press O and a card floats over the middle of every block. In Core this is the
    /// block's permanent territory name and quarter - the same chips and the same
    /// quarter plates the turf map prints, off the one shared rig
    /// (<see cref="TerritoryPlaques"/>) - and in the ordinary RoadDemo it keeps the
    /// generator's catalog diagnostics. Press O again and they go.
    ///
    /// The numbers on the diagnostic card come off RoadDemoBuilder.LotPlans - the plan
    /// BuildBlocks worked from - and are never measured back off the geometry: a bake is
    /// allowed a metre of overhang onto the sidewalk, so an AABB would answer with the
    /// building instead of the lot, which is the one number this overlay exists to show.
    ///
    /// Screen-space cards over world points, the same trick the police overlay uses:
    /// on a ScreenSpaceOverlay canvas a UI transform's position IS screen pixels, so
    /// WorldToScreenPoint feeds it straight in, the text stays crisp at every zoom
    /// and nothing has to billboard.
    /// </summary>
    public class DemoLotOverlay : MonoBehaviour
    {
        // Under the top bar's 20: these annotate the world, and the bar owns the
        // strip they would otherwise slide beneath.
        const int SortingOrder = 10;

        const float CardWidth = 224f;   // reference pixels on the 1080p design height
        const float CardHeight = 92f;

        /// <summary>The book owns the screen while it stands open - it takes the left
        /// half and the map the right, so a card over the world would land on one or
        /// the other. The cards drop while it is up and come back with it, the
        /// toggle's own state untouched.</summary>
        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen ||
                                TurfMapHud.IsOpen;

        /// <summary>One generator diagnostic card and the lot it stands over. The
        /// territory names are not in this list - they belong to the shared rig.
        /// </summary>
        struct LotCard
        {
            public Vector3 Centre;      // world, on the lot's own middle
            public RectTransform Rect;
        }

        readonly List<LotCard> _lots = new List<LotCard>();

        /// <summary>The city's own names, when it has a territory plan: the block chips
        /// and the quarter plates, built and written by the shared rig.</summary>
        readonly TerritoryPlaques _places = new TerritoryPlaques();

        RoadDemoBuilder _builder;
        GameObject _root;
        Canvas _canvas;
        Camera _cam;

        public void Init(RoadDemoBuilder builder) => _builder = builder;

        void Start()
        {
            bool hasCoreBlocks = TerritoryPlaques.Available(_builder);
            if (_builder == null || (!hasCoreBlocks && _builder.LotPlans.Count == 0))
            {
                // No plan to print - an overlay of nothing is worse than no overlay.
                Destroy(this);
                return;
            }

            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - the O lot overlay is off.");
                Destroy(this);
                return;
            }

            Build();
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 1280 x 720, the frame the design's own numbers are in and the one the
            // rest of the HUD family works in.
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Built ACTIVE and hidden at the end: a TMP text only loads its font in
            // OnEnable, which never runs under an inactive parent. All of it happens
            // inside this one call, so nothing renders in between.
            _root = new GameObject("Lot Cards", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            if (TerritoryPlaques.Available(_builder))
                _places.Build(_root.transform, _builder);
            else
                foreach (var lot in _builder.LotPlans)
                    _lots.Add(BuildCard(lot));

            // The switch is shared with the map, and O may already have been pressed
            // on the paper before this scene's street overlay was built.
            _root.SetActive(TerritoryPlaques.Shown);
        }

        LotCard BuildCard(RoadDemoBuilder.LotInfo lot)
        {
            var rect = DemoUi.NewRect($"Lot {lot.Column},{lot.Row}", _root.transform);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            DemoUi.Dress(face, DemoUi.Box, 15f, DemoUi.Panel);

            // The stripe says at a glance whether this size has a pad in the catalog:
            // gold for a lot something was composed for, dim steel for one nothing
            // ever was - the case worth spotting from across the map.
            bool hasPad = !string.IsNullOrEmpty(lot.Code);
            var stripe = DemoUi.Block(rect, "Accent", hasPad ? DemoUi.Gold : DemoUi.InkDim);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(13f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -22f);

            var title = Row(rect, "Code", 17f, hasPad ? DemoUi.Gold : DemoUi.InkDim,
                top: -8f, height: 24f, display: true);
            title.characterSpacing = 3f;
            title.text = hasPad
                ? $"{lot.Code}   {lot.Width:F0} x {lot.Depth:F0} m"
                : $"NO PAD   {lot.Width:F0} x {lot.Depth:F0} m";

            var cells = Row(rect, "Cells", 13f, DemoUi.InkDim, top: -33f, height: 19f);
            cells.text = $"{lot.CellsWide:F0} x {lot.CellsDeep:F0} cells";

            var contents = Row(rect, "Contents", 13f, DemoUi.Ink, top: -53f, height: 32f);
            // A packed row can name two or three bakes, which is longer than the card
            // is wide - wrap it rather than run it off the edge, and cut it with an
            // ellipsis rather than grow a card that has to sit over a block.
            contents.textWrappingMode = TextWrappingModes.Normal;
            contents.overflowMode = TextOverflowModes.Ellipsis;
            contents.text = lot.Contents;

            return new LotCard { Centre = Middle(lot), Rect = rect };
        }

        /// <summary>A line of the card, hung from its top edge and inset past the
        /// accent stripe.</summary>
        TMP_Text Row(RectTransform card, string name, float size, Color colour,
            float top, float height, bool display = false)
        {
            var text = DemoUi.Text(card, name, size, colour,
                TextAlignmentOptions.TopLeft, display);
            // Stretched across the card and hung from its top edge: the width comes
            // out 25 in from the left (clear of the accent stripe) and 15 from the
            // right, hence the 40 taken off and the 5 the row is nudged east.
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-40f, height);
            rect.anchoredPosition = new Vector2(5f, top);
            return text;
        }

        /// <summary>The lot's own middle, at street level: the card hangs off the
        /// centre of the interior, not the centre of whatever was built on it.</summary>
        static Vector3 Middle(RoadDemoBuilder.LotInfo lot) =>
            new Vector3(lot.Interior.center.x, 0f, lot.Interior.center.y);

        void Update()
        {
            if (BookOpen)
                return;

            var kb = Keyboard.current;
            if (kb != null && kb.oKey.wasPressedThisFrame)
            {
                bool shown = TerritoryPlaques.Toggle();
                _root.SetActive(shown);
                if (shown)
                    _places.Refresh(force: true);
                // paper, because that is what the plan is drawn as
                DemoAudio.Ui(DemoSounds.Paper);
            }
        }

        void LateUpdate()
        {
            if (!TerritoryPlaques.Shown)
            {
                if (_root.activeSelf)
                    _root.SetActive(false);
                return;
            }

            bool showRoot = !BookOpen;
            if (_root.activeSelf != showRoot) _root.SetActive(showRoot);
            if (!showRoot)
                return;

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            // Out in the street every block is large on the screen, so nothing is
            // thinned: the chips stand at whatever the camera is looking at.
            _places.Layout(Project);

            foreach (var card in _lots)
            {
                if (!Project(card.Centre, out var screen))
                {
                    if (card.Rect.gameObject.activeSelf)
                        card.Rect.gameObject.SetActive(false);
                    continue;
                }

                if (!card.Rect.gameObject.activeSelf)
                    card.Rect.gameObject.SetActive(true);
                card.Rect.position = new Vector3(screen.x, screen.y, 0f);
            }
        }

        /// <summary>Where the camera puts a world point. Behind the camera or off the
        /// viewport the whole card goes, rather than a stack of them piling up along
        /// the screen edge.</summary>
        bool Project(Vector3 world, out Vector2 screen)
        {
            var point = _cam.WorldToScreenPoint(world);
            screen = new Vector2(point.x, point.y);
            return point.z > 0f &&
                   point.x >= 0f && point.x <= Screen.width &&
                   point.y >= 0f && point.y <= Screen.height;
        }
    }
}
