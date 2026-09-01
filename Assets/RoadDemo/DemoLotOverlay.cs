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

        const float StemTall = 22f;
        const float MarkSize = 8f;

        struct Card
        {
            public Vector3 Centre;      // world, on the lot's own middle
            public RectTransform Rect;

            /// <summary>The block this marks, or null when the card is a quarter's
            /// name - the two live in one list because they are shown and hidden by
            /// one key and placed by one pass.</summary>
            public CoreBlockDefinition Block;
            public CoreQuarterDefinition Quarter;

            public Image[] Edge, PaperEdge;
            public Image Street;
            public RawImage Hatch, Stem;
            public TMP_Text Name, Figure;
            public Image Rule;
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

            var territory = _builder.Territories.Plan;
            if (territory != null && territory.Blocks.Count > 0)
            {
                for (int i = 0; i < territory.Blocks.Count; i++)
                    _cards.Add(BuildCard(territory.Blocks[i]));

                // The quarter's name goes down LAST, so it prints OVER the block chips.
                // It is the bigger reading of the two: which neighbourhood this is
                // survives a chip crossing it, and a chip does not survive being cut in
                // half by a name.
                //
                // Only quarters the plan actually filled. A quarter with no blocks has
                // no bounds either, so its label would stand at the world origin - and
                // every empty quarter in the roll would stand there together, printing
                // half a dozen names through one another.
                for (int i = 0; i < territory.Quarters.Count; i++)
                {
                    var quarter = territory.Quarters[i];
                    if (quarter.BlockIds.Count == 0 || quarter.LocalBounds.width <= 0f)
                        continue;
                    _cards.Add(BuildQuarterCard(quarter));
                }
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

        // ------------------------------------------------------ the design's marks

        /// <summary>The chip's ground and its shadow: near-black, a fifth of the city
        /// showing through, so a name laid over a bright pavement is still read.</summary>
        static readonly Color ChipFace = new Color(24f / 255f, 18f / 255f, 15f / 255f, 0.88f);

        /// <summary>What a block is to us, and the ink that says so at a glance. The
        /// design rules the chip in one of three: gold for a street that pays us, red
        /// for a rival's, and steel for one nobody has taken.</summary>
        enum Tone { Ours, Rival, Nobody }

        static Color EdgeOf(Tone tone) => tone switch
        {
            Tone.Ours => new Color(212f / 255f, 167f / 255f, 62f / 255f, 0.85f),
            Tone.Rival => LedgerStyle.RailRed,
            _ => new Color(144f / 255f, 131f / 255f, 123f / 255f, 0.7f),
        };

        static Color FigureOf(Tone tone) => tone switch
        {
            Tone.Ours => LedgerStyle.RailGold,
            Tone.Rival => LedgerStyle.RailRed,
            _ => LedgerStyle.RailNote,
        };

        /// <summary>
        /// One block, marked the way the design marks it: a chip hung over the middle
        /// of the block on a short stem, carrying the mark of who stands there, the
        /// block's name and whoever holds it, and what it is worth a week.
        ///
        /// The chip sizes itself to its words - a layout group and a fitter rather than
        /// a measured width - because the names are the plan's own and run from "Block
        /// 34" to "The laundry, Ash Street".
        /// </summary>
        Card BuildCard(CoreBlockDefinition block)
        {
            var bounds = _builder.Territories.WorldBounds(block.Id);

            // The stem's foot stands on the block; the chip rides above it.
            var rect = DemoUi.NewRect($"Block {block.Id}", _root.transform);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = Vector2.zero;

            var stem = DemoUi.NewRect("Stem", rect);
            stem.anchorMin = stem.anchorMax = new Vector2(0.5f, 0f);
            stem.pivot = new Vector2(0.5f, 0f);
            stem.anchoredPosition = Vector2.zero;
            stem.sizeDelta = new Vector2(1f, StemTall);
            var stemInk = stem.gameObject.AddComponent<RawImage>();
            stemInk.texture = LedgerStyle.FadeUp;
            stemInk.raycastTarget = false;

            var chip = DemoUi.NewRect("Chip", rect);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0f);
            chip.pivot = new Vector2(0.5f, 0f);
            chip.anchoredPosition = new Vector2(0f, StemTall);

            var face = chip.gameObject.AddComponent<Image>();
            face.color = ChipFace;
            face.raycastTarget = false;

            var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 3, 3);
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = chip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The design's two marks, built as a pair and shown one at a time: a SOLID
            // square for a street men of ours are standing on, and a ruled box with a
            // hatch through it for one we hold only on paper. The difference between a
            // fact and a claim, and it has to be legible at eight units across.
            var mark = DemoUi.NewRect("Mark", chip);
            var markElement = mark.gameObject.AddComponent<LayoutElement>();
            markElement.preferredWidth = markElement.preferredHeight = MarkSize;

            var street = DemoUi.NewRect("Street", mark);
            LedgerKit.Stretch(street);
            var streetInk = street.gameObject.AddComponent<Image>();
            streetInk.raycastTarget = false;

            var paper = DemoUi.NewRect("Paper", mark);
            LedgerKit.Stretch(paper);
            var hatch = LedgerKit.Texture(paper, LedgerStyle.Hatch, Color.white,
                MarkSize, MarkSize, 4f);
            var paperEdge = Border(paper, 1.5f);

            var name = ChipText(chip, "Name", 12f, LedgerV2.HeadCream, 14f);
            name.fontStyle = FontStyles.Bold;
            var figure = ChipText(chip, "Figure", 12f, LedgerStyle.RailGold, 8f);

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Rect = rect,
                Block = block,
                Edge = Border(chip, 1f),
                Street = streetInk,
                Hatch = hatch,
                PaperEdge = paperEdge,
                Stem = stemInk,
                Name = name,
                Figure = figure,
            };
        }

        /// <summary>
        /// The quarter's name, standing in the middle of it: the gothic in caps under a
        /// hairline, and beneath that how much of it is ours. Only type - a
        /// neighbourhood is not a thing you press, and the design gives it no chip.
        /// </summary>
        Card BuildQuarterCard(CoreQuarterDefinition quarter)
        {
            var bounds = _builder.Territories.WorldBounds(quarter.Id);

            var rect = DemoUi.NewRect($"Quarter {quarter.Name}", _root.transform);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(260f, 52f);

            var name = DemoUi.Text(rect, "Name", 17.4f, LedgerStyle.RailBright,
                TextAlignmentOptions.Bottom, display: true);
            name.font = LedgerStyle.Condensed;
            name.characterSpacing = 30f;
            name.text = quarter.Name.ToUpperInvariant();
            LedgerKit.PlaceTopLeft(name.rectTransform, 0f, 0f, 260f, 28f);

            // The rule the design draws under the name, and only as wide as the name -
            // it is that name's underline, not a divider across the card. TMP answers
            // for its own width before any layout pass, so the rule can be cut to it
            // here rather than a frame later.
            var ruled = Mathf.Min(260f, name.GetPreferredValues(name.text).x + 4f);
            var rule = DemoUi.NewRect("Rule", rect);
            LedgerKit.PlaceTopLeft(rule, (260f - ruled) * 0.5f, -28f, ruled, 1f);
            var ruleInk = rule.gameObject.AddComponent<Image>();
            ruleInk.color = new Color(219f / 255f, 206f / 255f, 196f / 255f, 0.5f);
            ruleInk.raycastTarget = false;

            var sub = DemoUi.Text(rect, "Sub", 10.8f, new Color(
                    219f / 255f, 206f / 255f, 196f / 255f, 0.7f),
                TextAlignmentOptions.Top, display: false);
            sub.font = LedgerStyle.Mono;
            sub.characterSpacing = 18f;
            LedgerKit.PlaceTopLeft(sub.rectTransform, 0f, -32f, 260f, 18f);

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Rect = rect,
                Quarter = quarter,
                Name = name,
                Figure = sub,
                Rule = ruleInk,
            };
        }

        /// <summary>The chip's hairline, as four rules laid on its edges. They sit
        /// OUTSIDE the layout - a border is not content, and a layout group handed one
        /// would space it like a word.</summary>
        static Image[] Border(RectTransform chip, float thickness)
        {
            var edges = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                var rect = DemoUi.NewRect("Edge", chip);
                rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                bool vertical = i >= 2;
                rect.anchorMin = new Vector2(vertical ? (i == 2 ? 0f : 1f) : 0f,
                                             vertical ? 0f : (i == 0 ? 1f : 0f));
                rect.anchorMax = new Vector2(vertical ? (i == 2 ? 0f : 1f) : 1f,
                                             vertical ? 1f : (i == 0 ? 1f : 0f));
                rect.pivot = new Vector2(vertical ? (i == 2 ? 0f : 1f) : 0.5f,
                                         vertical ? 0.5f : (i == 0 ? 1f : 0f));
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = vertical
                    ? new Vector2(thickness, 0f)
                    : new Vector2(0f, thickness);
                var ink = rect.gameObject.AddComponent<Image>();
                ink.raycastTarget = false;
                edges[i] = ink;
            }
            return edges;
        }

        // ------------------------------------------------------------- what they say

        /// <summary>The state of the city the cards were last written against, so a
        /// hundred blocks are not re-read every frame the overlay is up.</summary>
        int _readVersion = -1;

        /// <summary>
        /// Fill every card from the territory ledger: who leads the block, whether
        /// anyone of ours is standing on it, and what it pays us a week. One reading
        /// from one authority - the same TerritoryControl the plate draws its wash from
        /// - so the chip over a block and the colour under it can never disagree.
        /// </summary>
        void Refresh(bool force)
        {
            var runtime = TerritoryRuntime.Instance;
            var control = runtime != null ? runtime.Control : null;
            var geography = runtime != null ? runtime.Geography : null;
            if (control == null || geography == null)
                return;

            if (!force && runtime.StateVersion == _readVersion)
                return;
            _readVersion = runtime.StateVersion;

            var player = new LivingCity.Territory.TerritoryGangId(
                LivingCity.Gangs.GangCatalog.PlayerGangId);

            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card.Block != null)
                    WriteBlock(card, control, geography, runtime, player);
                else if (card.Quarter != null)
                    WriteQuarter(card, control, geography, player);
            }
        }

        void WriteBlock(Card card, LivingCity.Territory.TerritoryControlLedger control,
            LivingCity.Territory.ITerritoryGeography geography, TerritoryRuntime runtime,
            LivingCity.Territory.TerritoryGangId player)
        {
            var point = new LivingCity.Territory.TerritoryPoint(card.Centre.x, card.Centre.z);
            if (!geography.TryGetBlockAt(point, out var blockId))
                return;

            var leader = control.LeaderOf(blockId);
            var state = control.StateOf(blockId);
            var tone = !leader.IsValid ? Tone.Nobody
                : leader.Equals(player) ? Tone.Ours
                : Tone.Rival;

            // The name is the block's own; who holds it and what it pays go in the
            // figure beside it, which is where the design puts them - "BLOCK 34" then
            // "Nobody's", "THE LAUNDRY, ASH ST" then "$1,247 booked".
            card.Name.text = card.Block.Name.ToUpperInvariant();

            var week = tone == Tone.Ours ? WeeklyTake(geography, runtime, blockId, player) : 0;
            card.Figure.text = week > 0 ? "$" + week + " a week"
                : tone == Tone.Ours ? "Ours"
                : tone == Tone.Rival ? LivingCity.Gangs.GangCatalog.Names[leader.Value]
                : "Nobody's";
            card.Figure.color = FigureOf(tone);

            // Standing on it, or only written down as ours.
            bool standing = state == LivingCity.Territory.TerritoryControlState.Controlled;
            var ink = tone == Tone.Ours ? LedgerV2.Red
                : tone == Tone.Rival ? LedgerStyle.RailRed
                : LedgerStyle.RailLabel;

            if (card.Street.gameObject.activeSelf != standing)
                card.Street.gameObject.SetActive(standing);
            if (card.Hatch.transform.parent.gameObject.activeSelf == standing)
                card.Hatch.transform.parent.gameObject.SetActive(!standing);
            card.Street.color = ink;
            card.Hatch.color = ink;
            for (var e = 0; e < card.PaperEdge.Length; e++)
                card.PaperEdge[e].color = ink;

            var edge = EdgeOf(tone);
            for (var e = 0; e < card.Edge.Length; e++)
                card.Edge[e].color = edge;
            card.Stem.color = edge;
        }

        /// <summary>What the shops on this block pay us in a week - the dues ledger's
        /// own weekly rate, summed, and never a figure this overlay works out for
        /// itself.</summary>
        static int WeeklyTake(LivingCity.Territory.ITerritoryGeography geography,
            TerritoryRuntime runtime, LivingCity.Territory.TerritoryBlockId blockId,
            LivingCity.Territory.TerritoryGangId player)
        {
            var dues = runtime.Dues;
            // Which shops stand on a block is the full geography's answer, not the
            // interface's - the interface carries the shape of the city, not what has
            // been built on it. No concrete geography, no figure, and the chip simply
            // says nothing rather than a number nobody stands behind.
            var placed = geography as LivingCity.Territory.TerritoryGeography;
            if (dues == null || placed == null)
                return 0;

            var week = 0;
            var placements = placed.BusinessesOf(blockId);
            for (var i = 0; i < placements.Count; i++)
                if (dues.TryGet(placements[i].BusinessId, out var account) &&
                    account.GangId.Equals(player))
                    week += account.WeeklyRate;
            return week;
        }

        void WriteQuarter(Card card, LivingCity.Territory.TerritoryControlLedger control,
            LivingCity.Territory.ITerritoryGeography geography,
            LivingCity.Territory.TerritoryGangId player)
        {
            var blocks = card.Quarter.BlockIds;
            var ours = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                var bounds = _builder.Territories.WorldBounds(blocks[i]);
                var point = new LivingCity.Territory.TerritoryPoint(
                    bounds.center.x, bounds.center.y);
                if (geography.TryGetBlockAt(point, out var id) &&
                    control.LeaderOf(id).Equals(player))
                    ours++;
            }

            card.Figure.text = blocks.Count +
                (blocks.Count == 1 ? " BLOCK" : " BLOCKS") + "  \u00b7  " +
                (ours == 0 ? "NONE PAYS US" : ours + " PAYS US");
        }

        /// <summary>How tall a word is allowed to make the chip. TMP's own preferred
        /// height is the LINE box - ascender to descender plus leading - which is half
        /// as tall again as the capitals actually printed, and it is generous at the
        /// top. Left to it the chip came out a third too deep and its type rode high in
        /// the box. Pinned to the band the capitals occupy, the chip is the design's
        /// height and the type sits on its middle.</summary>
        const float ChipLine = 13f;

        static TMP_Text ChipText(Transform chip, string name, float size, Color colour,
            float spacing)
        {
            var text = DemoUi.Text(chip, name, size, colour,
                TextAlignmentOptions.MidlineLeft, display: false);
            text.font = LedgerStyle.Mono;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var element = text.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = ChipLine;

            text.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            return text;
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
                if (_shown)
                    Refresh(force: true);
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
