using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// Press O and a card floats over the middle of every block. In Core this is the
    /// block's permanent territory name and quarter; in the ordinary RoadDemo it keeps
    /// the generator's catalog diagnostics. Press O again and they go.
    ///
    /// The numbers come off RoadDemoBuilder.LotPlans - the plan BuildBlocks worked
    /// from - and are never measured back off the geometry: a bake is allowed a metre
    /// of overhang onto the sidewalk, so an AABB would answer with the building
    /// instead of the lot, which is the one number this overlay exists to show.
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

        struct Card
        {
            public Vector3 Centre;      // world, on the lot's own middle
            public RectTransform Rect;
        }

        readonly List<Card> _cards = new List<Card>();
        RoadDemoBuilder _builder;
        GameObject _root;
        Canvas _canvas;
        Camera _cam;
        bool _shown;

        public void Init(RoadDemoBuilder builder) => _builder = builder;

        void Start()
        {
            bool hasCoreBlocks = _builder != null &&
                                 _builder.Territories.Plan != null &&
                                 _builder.Territories.Plan.Blocks.Count > 0;
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
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Built ACTIVE and hidden at the end: a TMP text only loads its font in
            // OnEnable, which never runs under an inactive parent. All of it happens
            // inside this one call, so nothing renders in between.
            _root = new GameObject("Lot Cards", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var territory = _builder.Territories.Plan;
            if (territory != null && territory.Blocks.Count > 0)
            {
                for (int i = 0; i < territory.Blocks.Count; i++)
                    _cards.Add(BuildCard(territory.Blocks[i]));
            }
            else
            {
                foreach (var lot in _builder.LotPlans)
                    _cards.Add(BuildCard(lot));
            }

            _root.SetActive(false);
            _shown = false;
        }

        Card BuildCard(RoadDemoBuilder.LotInfo lot)
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

            return new Card { Centre = Middle(lot), Rect = rect };
        }

        /// <summary>A compact 3D-city indicator sourced from the same immutable block
        /// definition combat and both maps use.</summary>
        Card BuildCard(CoreBlockDefinition block)
        {
            var bounds = _builder.Territories.WorldBounds(block.Id);
            var rect = DemoUi.NewRect($"Block {block.Id}", _root.transform);
            rect.sizeDelta = new Vector2(250f, 58f);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            DemoUi.Dress(face, DemoUi.Box, 14f, DemoUi.Panel);

            var stripe = DemoUi.Block(rect, "Quarter", DemoUi.Gold);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = new Vector2(13f, 0f);
            stripeRect.sizeDelta = new Vector2(3f, -16f);

            var title = Row(rect, "Name", 16f, DemoUi.Gold,
                top: -7f, height: 23f, display: true);
            title.characterSpacing = 2f;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.text = block.Name.ToUpperInvariant();

            var quarter = _builder.Territories.Quarter(block.QuarterId);
            var subtitle = Row(rect, "Quarter Name", 12f, DemoUi.InkDim,
                top: -31f, height: 18f);
            subtitle.characterSpacing = 8f;
            subtitle.text = quarter != null
                ? quarter.Name.ToUpperInvariant() + " QUARTER"
                : "CORE BLOCK";

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Rect = rect,
            };
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
                _shown = !_shown;
                _root.SetActive(_shown);
                // paper, because that is what the plan is drawn as
                DemoAudio.Ui(DemoSounds.Paper);
            }
        }

        void LateUpdate()
        {
            if (!_shown)
                return;

            bool showRoot = !BookOpen;
            if (_root.activeSelf != showRoot) _root.SetActive(showRoot);
            if (!showRoot)
                return;

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            float w = Screen.width, h = Screen.height;
            foreach (var card in _cards)
            {
                var screen = _cam.WorldToScreenPoint(card.Centre);

                // Behind the camera or off the viewport: the whole card goes, rather
                // than a stack of them piling up along the screen edge.
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                var go = card.Rect.gameObject;
                if (go.activeSelf != on) go.SetActive(on);
                if (!on) continue;

                card.Rect.position = new Vector3(screen.x, screen.y, 0f);
            }
        }
    }
}
