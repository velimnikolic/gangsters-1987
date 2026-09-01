using System.Collections.Generic;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The names the city carries on it: a chip over the middle of every block, and
    /// the quarter's name standing in the middle of the neighbourhood.
    ///
    /// ONE rig, used by both screens that print them - the O overlay out in the street
    /// and the turf map's own paper. They are the same reading of the same ledger, so
    /// they are the same code: a chip whose rule says who holds this block, whose mark
    /// says whether men of ours are actually standing on it, and a quarter name with
    /// how much of it pays us set under a hairline. Two copies of this drifted apart
    /// the moment either was touched, which is exactly what the project's shared-system
    /// rule exists to stop.
    ///
    /// What differs between the two hosts is only the PROJECTION - the street overlay
    /// asks the camera where a world point lands, the map asks its own survey - and how
    /// tightly block chips have to be thinned when the ground gets small. Both are
    /// handed in at <see cref="Layout"/>; nothing in here knows about a camera or a
    /// plate.
    ///
    /// The cards are built once and MOVED per frame. Building is what costs; a position
    /// is two floats.
    /// </summary>
    public sealed class TerritoryPlaques
    {
        /// <summary>How tall the chip's stem is, and how big the mark at the head of a
        /// chip is - the design's own numbers, in the 1280 x 720 frame every HUD in this
        /// game is drawn in.</summary>
        const float StemTall = 22f, MarkSize = 8f;

        /// <summary>How wide a quarter's name plate is, and how much of it the name and
        /// the figure under it take.</summary>
        const float QuarterWide = 260f, QuarterTall = 52f;

        /// <summary>How tall a word is allowed to make the chip. TMP's own preferred
        /// height is the LINE box - ascender to descender plus leading - which is half
        /// as tall again as the capitals actually printed, and it is generous at the
        /// top. Left to it the chip came out a third too deep and its type rode high in
        /// the box. Pinned to the band the capitals occupy, the chip is the design's
        /// height and the type sits on its middle.</summary>
        const float ChipLine = 13f;

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

        /// <summary>One name on the city: either a block's chip or a quarter's plate,
        /// never both. They live in one list because they are shown, hidden and placed
        /// by one pass.</summary>
        struct Card
        {
            public Vector3 Centre;       // world, on the middle of the ground it names
            public Vector2 Ground;       // how many metres across that ground is
            public RectTransform Rect;

            public CoreBlockDefinition Block;
            public CoreQuarterDefinition Quarter;
            public CoreDecorationDefinition Decoration;

            public Image[] Edge, PaperEdge;
            public Image Street;
            public RawImage Hatch, Stem;
            public TMP_Text Name, Figure;
        }

        readonly List<Card> _cards = new List<Card>();
        RoadDemoBuilder _builder;

        /// <summary>The state of the city the cards were last written against, so a
        /// hundred blocks are not re-read every frame they are up.</summary>
        int _readVersion = -1;

        public int Count => _cards.Count;

        /// <summary>
        /// Whether the city's names are up. ONE switch for both screens, not one per
        /// rig: O is the same key over the street and over the plate, so pulling the
        /// wheel up to the map has to find the names exactly as they were left down in
        /// the street, and pressing O on the paper has to put them away for both.
        ///
        /// Static state outlives Play with domain reload off - the OverlayRegistry
        /// rule - so it is reset on the way in rather than trusted to start false.
        /// </summary>
        public static bool Shown { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Shown = false;

        /// <summary>The O key's verb. It makes no sound and touches no object: the
        /// screen that read the key owns its own paper and its own root, because only
        /// it knows whether anything else has the screen at that moment.</summary>
        public static bool Toggle()
        {
            Shown = !Shown;
            return Shown;
        }

        /// <summary>Whether this city has a territory plan to print at all. A city
        /// generated without one keeps whatever its host printed before.</summary>
        public static bool Available(RoadDemoBuilder builder) =>
            builder != null && builder.Territories.Plan != null &&
            builder.Territories.Plan.Blocks.Count > 0;

        /// <summary>
        /// Sets the whole city's names down under one parent. Blocks first and quarters
        /// LAST, so a quarter's name prints OVER the chips: it is the bigger reading of
        /// the two - which neighbourhood this is survives a chip crossing it, and a chip
        /// does not survive being cut in half by a name.
        ///
        /// Only quarters the plan actually filled. A quarter with no blocks has no
        /// bounds either, so its label would stand at the world origin - and every empty
        /// quarter in the roll would stand there together, printing half a dozen names
        /// through one another.
        /// </summary>
        public void Build(Transform parent, RoadDemoBuilder builder)
        {
            _builder = builder;
            _cards.Clear();
            _readVersion = -1;
            if (parent == null || !Available(builder))
                return;

            var plan = builder.Territories.Plan;
            for (int i = 0; i < plan.Blocks.Count; i++)
                _cards.Add(BuildChip(parent, plan.Blocks[i]));

            // The dressing says what it is and nothing more: PARK over a park, in the
            // unclaimed tones, with no mark and no ledger behind it. It is not a block,
            // so it never carries a number and Refresh never writes to it.
            for (int i = 0; i < plan.Decorations.Count; i++)
                _cards.Add(BuildPlaque(parent, plan.Decorations[i]));

            for (int i = 0; i < plan.Quarters.Count; i++)
            {
                var quarter = plan.Quarters[i];
                if (quarter.BlockIds.Count == 0 || quarter.LocalBounds.width <= 0f)
                    continue;
                _cards.Add(BuildQuarter(parent, quarter));
            }
        }

        /// <summary>Forgets every card. The objects themselves belong to the parent the
        /// host handed in, and go down with it.</summary>
        public void Clear()
        {
            _cards.Clear();
            _readVersion = -1;
        }

        // ------------------------------------------------------------- the marks

        /// <summary>
        /// One block, marked the way the design marks it: a chip hung over the middle
        /// of the block on a short stem, carrying the mark of who stands there and the
        /// block's own name.
        ///
        /// The chip sizes itself to its words - a layout group and a fitter rather than
        /// a measured width - because the names are the plan's own and run from "Block
        /// 34" to "The laundry, Ash Street".
        /// </summary>
        Card BuildChip(Transform parent, CoreBlockDefinition block)
        {
            var bounds = _builder.Territories.WorldBounds(block.Id);

            // The stem's foot stands on the block; the chip rides above it.
            var rect = DemoUi.NewRect($"Block {block.Id}", parent);
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

            // Struck with its name and in the unclaimed tones from the start. The
            // ledger answers a frame or two later, and a chip built blank stands as an
            // empty black box over the street until it does.
            name.text = block.Name.ToUpperInvariant();
            var edges = Border(chip, 1f);
            var idle = EdgeOf(Tone.Nobody);
            for (var e = 0; e < edges.Length; e++)
                edges[e].color = idle;
            stemInk.color = idle;
            streetInk.color = LedgerStyle.RailLabel;
            hatch.color = LedgerStyle.RailLabel;
            for (var e = 0; e < paperEdge.Length; e++)
                paperEdge[e].color = LedgerStyle.RailLabel;
            street.gameObject.SetActive(false);

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Ground = new Vector2(bounds.width, bounds.height),
                Rect = rect,
                Block = block,
                Edge = edges,
                Street = streetInk,
                Hatch = hatch,
                PaperEdge = paperEdge,
                Stem = stemInk,
                Name = name,
            };
        }

        /// <summary>
        /// The dressing's own plate: the same chip as a block's, struck once with the word
        /// the ground IS - PARK - and left in the unclaimed tones for good. No mark, because
        /// there is nothing to stand on the paper about; no number, because it has no block
        /// id to print; and no entry in the ledger, so <see cref="Refresh"/> passes it by.
        /// </summary>
        Card BuildPlaque(Transform parent, CoreDecorationDefinition decoration)
        {
            var bounds = _builder.Territories.WorldBounds(decoration.LocalBounds);

            var rect = DemoUi.NewRect($"Decoration {decoration.Label}", parent);
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
            stemInk.color = EdgeOf(Tone.Nobody);

            var chip = DemoUi.NewRect("Chip", rect);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0f);
            chip.pivot = new Vector2(0.5f, 0f);
            chip.anchoredPosition = new Vector2(0f, StemTall);

            var face = chip.gameObject.AddComponent<Image>();
            face.color = ChipFace;
            face.raycastTarget = false;

            var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 3, 3);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = chip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var name = ChipText(chip, "Name", 12f, LedgerStyle.RailLabel, 14f);
            name.text = decoration.Label.ToUpperInvariant();

            var edges = Border(chip, 1f);
            var idle = EdgeOf(Tone.Nobody);
            for (var e = 0; e < edges.Length; e++)
                edges[e].color = idle;

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Ground = new Vector2(bounds.width, bounds.height),
                Rect = rect,
                Decoration = decoration,
                Edge = edges,
                Stem = stemInk,
                Name = name,
            };
        }

        /// <summary>
        /// The quarter's name, standing in the middle of it: the gothic in caps under a
        /// hairline, and beneath that how much of it is ours. Only type - a
        /// neighbourhood is not a thing you press, and the design gives it no chip.
        /// </summary>
        Card BuildQuarter(Transform parent, CoreQuarterDefinition quarter)
        {
            var bounds = _builder.Territories.WorldBounds(quarter.Id);

            var rect = DemoUi.NewRect($"Quarter {quarter.Name}", parent);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(QuarterWide, QuarterTall);

            var name = DemoUi.Text(rect, "Name", 17.4f, LedgerStyle.RailBright,
                TextAlignmentOptions.Bottom, display: true);
            name.font = LedgerStyle.Condensed;
            name.characterSpacing = 30f;
            name.text = quarter.Name.ToUpperInvariant();
            LedgerKit.PlaceTopLeft(name.rectTransform, 0f, 0f, QuarterWide, 28f);

            // The rule the design draws under the name, and only as wide as the name -
            // it is that name's underline, not a divider across the card. TMP answers
            // for its own width before any layout pass, so the rule can be cut to it
            // here rather than a frame later.
            var ruled = Mathf.Min(QuarterWide, name.GetPreferredValues(name.text).x + 4f);
            var rule = DemoUi.NewRect("Rule", rect);
            LedgerKit.PlaceTopLeft(rule, (QuarterWide - ruled) * 0.5f, -28f, ruled, 1f);
            var ruleInk = rule.gameObject.AddComponent<Image>();
            ruleInk.color = new Color(219f / 255f, 206f / 255f, 196f / 255f, 0.5f);
            ruleInk.raycastTarget = false;

            var sub = DemoUi.Text(rect, "Sub", 10.8f, new Color(
                    219f / 255f, 206f / 255f, 196f / 255f, 0.7f),
                TextAlignmentOptions.Top, display: false);
            sub.font = LedgerStyle.Mono;
            sub.characterSpacing = 18f;
            sub.text = quarter.BlockIds.Count +
                (quarter.BlockIds.Count == 1 ? " BLOCK" : " BLOCKS");
            LedgerKit.PlaceTopLeft(sub.rectTransform, 0f, -32f, QuarterWide, 18f);

            return new Card
            {
                Centre = new Vector3(bounds.center.x, 0f, bounds.center.y),
                Ground = new Vector2(bounds.width, bounds.height),
                Rect = rect,
                Quarter = quarter,
                Name = name,
                Figure = sub,
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

        // ------------------------------------------------------------- what they say

        /// <summary>
        /// Fill every card from the territory ledger: who leads the block, whether
        /// anyone of ours is standing on it, and how much of a quarter pays us. One
        /// reading from one authority - the same TerritoryControl the plate draws its
        /// wash from - so the chip over a block and the colour under it can never
        /// disagree.
        /// </summary>
        public void Refresh(bool force)
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
                    WriteBlock(card, control, geography, player);
                else if (card.Quarter != null)
                    WriteQuarter(card, control, geography, player);
            }
        }

        void WriteBlock(Card card, LivingCity.Territory.TerritoryControlLedger control,
            LivingCity.Territory.ITerritoryGeography geography,
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

            // The chip carries the block's name and nothing else in words. WHO holds
            // it is said by colour alone - the rule round the chip, the stem under it,
            // the mark at its head - and by the mark's two shapes. The family's name
            // beside every block was the same fact printed twice, and it doubled the
            // width of a label that has to sit over a street.
            card.Name.text = card.Block.Name.ToUpperInvariant();

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

        // ---------------------------------------------------------------- placing

        /// <summary>Where a world point lands on the screen, in REAL screen pixels, and
        /// whether it is on the screen at all. The street overlay answers with its
        /// camera; the map answers with its own survey projection.</summary>
        public delegate bool Projector(Vector3 world, out Vector2 screen);

        /// <summary>How few pixels of ground a block may be reduced to and still be
        /// worth naming - about what a chip itself measures across. Under it the label
        /// is wider than the block it points at, the city disappears under its own
        /// lettering, and the chips stand down and leave the map to the quarter
        /// names.</summary>
        const float MinBlockPixels = 120f;

        /// <summary>
        /// Every card over the ground it names, and hidden rather than left hanging in
        /// the margin when that ground has gone off the screen.
        ///
        /// <paramref name="pixelsPerMetre"/> is how small the ground has become, and it
        /// thins the block chips out as the view pulls back; hand in a non-positive
        /// number where that does not apply and every chip stands.
        /// </summary>
        public void Layout(Projector project, float pixelsPerMetre = 0f)
        {
            if (project == null)
                return;

            Refresh(force: false);

            bool thin = pixelsPerMetre > 0f;
            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card.Rect == null)
                    continue;

                bool on = project(card.Centre, out var screen);
                if (on && thin && card.Quarter == null)
                    on = pixelsPerMetre * Mathf.Min(card.Ground.x, card.Ground.y) >=
                         MinBlockPixels;

                var go = card.Rect.gameObject;
                if (go.activeSelf != on)
                    go.SetActive(on);
                if (on)
                    card.Rect.position = new Vector3(screen.x, screen.y, 0f);
            }
        }
    }
}
