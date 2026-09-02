using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The 1987 ledger's drawing kit, second edition.
    ///
    /// The first edition drew a document: manila stock, a fall of light down every
    /// sheet, grain, foxing, punched holes, a coffee ring, and a card tilted an eighth
    /// of a degree off square. This one draws a TERMINAL. Flat fills, hard edges, one
    /// drop shadow to say a panel is laid on the ground and nothing else. No texture,
    /// no tilt, no aging, no paper.
    ///
    /// Three faces and they never mix:
    ///   Condensed (Oswald)   names, headings, figures a reader scans
    ///   Mono (IBM Plex)      labels, columns, stamps, everything measured
    ///   Serif (PT Serif)     copy a reader READS - a blurb, an intelligence note
    ///
    /// Every colour is the design's own oklch converted once, in LedgerStyle or here.
    /// Nothing in this file eyeballs a value, and nothing in it calls the first
    /// edition's paper helpers.
    /// </summary>
    public static class LedgerV2
    {
        // ------------------------------------------------------------------ palette

        static Color Rgb(int hex) => new Color(
            ((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        /// <summary>The same conversion, for a page that carries a colour of its own -
        /// the block file's four ownership inks, say. It is still the design's oklch,
        /// converted once and written as the sRGB it comes out as; nothing eyeballs a
        /// value and nothing tints one at runtime.</summary>
        public static Color Rgb2(int hex) => Rgb(hex);

        /// <summary>A panel laid on the sheet, and the two bands it is striped with.</summary>
        public static readonly Color Panel = Rgb(0xf4efe9);
        public static readonly Color PanelBand = Rgb(0xede7df);
        public static readonly Color PanelDark = Rgb(0xe7e0d9);

        /// <summary>A panel's dark head band, and the two weights of type on it.</summary>
        public static readonly Color Head = Rgb(0x18120f);
        public static readonly Color HeadInk = Rgb(0xdbcec4);
        public static readonly Color HeadDim = Rgb(0x90837b);
        public static readonly Color HeadCream = Rgb(0xf3ede7);

        /// <summary>Rules: the hairline inside a panel, the dotted leader between a
        /// label and its figure, and the heavier rule that closes a section.</summary>
        public static readonly Color Rule = Rgb(0xc5bcb4);
        public static readonly Color Hair = Rgb(0xd8cfc7);
        public static readonly Color Dotted = Rgb(0xc1b5ab);
        public static readonly Color SheetRule = Rgb(0xa2968c);

        /// <summary>Ink, in the four weights the sheet uses.</summary>
        public static readonly Color Ink = Rgb(0x1a1512);
        public static readonly Color Body = Rgb(0x231e1b);
        public static readonly Color Muted = Rgb(0x675b53);
        public static readonly Color Label = Rgb(0x6d6059);
        public static readonly Color Faint = Rgb(0x7b6f67);

        /// <summary>The three readings: bad, watch it, good.</summary>
        public static readonly Color Red = Rgb(0xac3031);
        public static readonly Color Amber = Rgb(0xa66d00);
        public static readonly Color Green = Rgb(0x146720);

        /// <summary>What is written on PAPER, as against what is true on the street.</summary>
        public static readonly Color PaperBlue = Rgb(0x364452);

        /// <summary>A row the reader has picked, and a row the sheet is complaining
        /// about.</summary>
        public static readonly Color Picked = Rgb(0xebddb9);
        public static readonly Color Wrong = Rgb(0xfbe2d9);

        /// <summary>The money sheets: the two columns, their stripe, and the band the
        /// profit is struck in.</summary>
        public static readonly Color Money = Rgb(0xe5f3e9);
        public static readonly Color MoneyStripe = Rgb(0xdcede1);
        public static readonly Color MoneyRule = Rgb(0xcbd8cd);
        public static readonly Color MoneyEdge = Rgb(0x1a3520);
        public static readonly Color ProfitBand = Rgb(0xf1d8cf);

        /// <summary>The stock book, which is a carbon copy and reads like one.</summary>
        public static readonly Color Carbon = Rgb(0xfceae9);
        public static readonly Color CarbonRule = Rgb(0xd7bdbc);
        public static readonly Color CarbonDotted = Rgb(0xcab0af);
        public static readonly Color CarbonInk = Rgb(0x662f30);
        public static readonly Color CarbonLabel = Rgb(0x745d5c);

        /// <summary>Plates: the empty ground a photograph or a catalogue cut sits on.</summary>
        public static readonly Color Plate = Rgb(0xdcd3c6);
        public static readonly Color PressPlate = Rgb(0xc2b5a2);
        public static readonly Color Thumb = Rgb(0xc7bbb1);
        public static readonly Color Portrait = Rgb(0xcec2b7);
        public static readonly Color DarkPlate = Rgb(0x39312c);
        public static readonly Color DarkPlateInk = Rgb(0x9c8f87);
        /// <summary>The warm empty stage behind the filmed block and the Boss card.</summary>
        public static readonly Color FilmPlate = Rgb(0x261810);

        /// <summary>The till roll, and the hand that signs the books.</summary>
        public static readonly Color Tape = Rgb(0xf8f5ef);
        public static readonly Color Signature = Rgb(0x213c59);

        /// <summary>A lieutenant's rank flash, and the boss's.</summary>
        public static readonly Color Lieutenant = Rgb(0x624727);
        public static readonly Color Boss = Rgb(0xea6a64);

        /// <summary>A filing still awaiting a ruling.</summary>
        public static readonly Color Filed = Rgb(0x4075aa);

        /// <summary>The block ledger's column heads, printed on the dark band in the
        /// colour of the thing each column reports: what the paper says is blue, what
        /// the street says is green, and the rest is plain cream.</summary>
        public static readonly Color HeadPaper = Rgb(0xa0bbd7);
        public static readonly Color HeadStreet = Rgb(0x99ce9a);

        /// <summary>Copy set for reading, and the caption under a cut.</summary>
        public static readonly Color Copy = Rgb(0x37322e);
        public static readonly Color Caption = Rgb(0x49403b);

        /// <summary>The trough a meter fills on light ground.</summary>
        public static readonly Color Trough = Rgb(0xc5bcb4);

        // ------------------------------------------------------------------ panels

        /// <summary>
        /// A panel: a flat card laid on the sheet under the design's two-layer drop
        /// shadow - a tight contact shadow and a wide soft one. No stock, no grain, no
        /// tilt: what makes this read as laid ON something is the shadow and nothing
        /// else.
        /// </summary>
        public static RectTransform Card(string name, Transform parent, float x, float y,
            float w, float h, Color? face = null)
        {
            var rect = NewRect(name, parent);
            PlaceTopLeft(rect, x, y, w, h);
            Shadow(rect, "Cast", 12f, new Vector2(0f, -4f), 0.26f);
            Shadow(rect, "Contact", 3f, new Vector2(0f, -1f), 0.42f);
            var fill = NewRect("Face", rect);
            Stretch(fill);
            Fill(fill, face ?? Panel);
            return rect;
        }

        /// <summary>One layer of a panel's shadow, inside the panel's own rect so it
        /// hides and dies with it.</summary>
        static void Shadow(RectTransform panel, string name, float spread, Vector2 offset,
            float strength)
        {
            var shadow = NewRect(name, panel);
            shadow.anchorMin = Vector2.zero;
            shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-spread + offset.x, -spread + offset.y);
            shadow.offsetMax = new Vector2(spread + offset.x, spread + offset.y);
            var image = shadow.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.SoftShadow;
            image.type = Image.Type.Sliced;
            image.color = new Color(0f, 0f, 0f, strength);
            image.raycastTarget = false;
        }

        /// <summary>A panel's dark head band: what the panel is on the left, and a
        /// stamp or a count held to the right. Answers the y below it.</summary>
        public static float CardHead(RectTransform card, float w, string label,
            string right = "", float h = 30f, Color? ink = null)
        {
            var band = NewRect("Head", card);
            PlaceTopLeft(band, 0f, 0f, w, h);
            Fill(band, Head);
            var text = Caps(band, 16f, -(h - 12f) * 0.5f, w - 200f, label, 10f,
                ink ?? HeadInk, 13f);
            text.font = LedgerStyle.MonoBold;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (right.Length > 0)
            {
                var note = Caps(band, w - 216f, -(h - 12f) * 0.5f, 200f, right, 9.5f,
                    HeadDim, 8f, TextAlignmentOptions.MidlineRight);
                note.font = LedgerStyle.Mono;
                note.overflowMode = TextOverflowModes.Ellipsis;
            }
            return -h;
        }

        // ------------------------------------------------------------------- heads

        /// <summary>
        /// A page's head: its name in the condensed gothic, the line of mono under it
        /// that says what the page IS, and the heavy rule that closes the pair.
        /// Answers the y below the rule.
        /// </summary>
        public static float PageHead(Transform parent, float x, float y, float w,
            string title, string sub)
        {
            var head = Line(parent, LedgerStyle.Condensed, 27f, Ink, x, y, w * 0.6f, 36f,
                title.ToUpperInvariant());
            head.characterSpacing = 2f;

            var note = Caps(parent, x, y - 34f, w * 0.72f, sub, 11f, Muted, 2f);
            note.font = LedgerStyle.Mono;
            note.overflowMode = TextOverflowModes.Ellipsis;

            var ruleY = y - 56f;
            Block("Head rule", parent, x, ruleY, w, 3f, Ink);
            return ruleY - 16f;
        }

        /// <summary>A numbered section inside a page - the design's "I. CHAIN OF
        /// COMMAND" - with its aside held to the right margin over a hairline.
        /// Answers the y below the rule.</summary>
        public static float Section(Transform parent, float x, float y, float w,
            string title, string aside = "")
        {
            var head = Line(parent, LedgerStyle.Condensed, 19f, Ink, x, y, w * 0.55f, 26f,
                title.ToUpperInvariant());
            head.characterSpacing = 4f;
            if (aside.Length > 0)
            {
                var note = Caps(parent, x + w * 0.45f, y + 3f, w * 0.55f, aside, 11f,
                    Muted, 2f, TextAlignmentOptions.MidlineRight);
                note.font = LedgerStyle.Mono;
                note.overflowMode = TextOverflowModes.Ellipsis;
            }
            var ruleY = y - 28f;
            Block("Section rule", parent, x, ruleY, w, 1f, SheetRule);
            return ruleY - 14f;
        }

        // -------------------------------------------------------------------- type

        /// <summary>A mono label - a column head, a stamp, anything measured.</summary>
        public static TextMeshProUGUI Mono(Transform parent, float x, float y, float w,
            string text, float size = 10.5f, Color? colour = null, float spacing = 5f,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var label = Line(parent, LedgerStyle.Mono, size, colour ?? Label, x, y, w,
                LineBox(size), text, align);
            label.characterSpacing = spacing;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>A figure: mono, bold, and usually held to a right margin.</summary>
        public static TextMeshProUGUI Figure(Transform parent, float x, float y, float w,
            string text, float size = 12.5f, Color? colour = null,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineRight)
        {
            var figure = Line(parent, LedgerStyle.MonoBold, size, colour ?? Ink, x, y, w,
                LineBox(size), text, align);
            figure.overflowMode = TextOverflowModes.Ellipsis;
            return figure;
        }

        /// <summary>A name, or anything else a reader scans rather than reads.</summary>
        public static TextMeshProUGUI Name(Transform parent, float x, float y, float w,
            string text, float size = 17f, Color? colour = null,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var name = Line(parent, LedgerStyle.Condensed, size, colour ?? Ink, x, y, w,
                LineBox(size), text, align);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            return name;
        }

        /// <summary>Copy a reader actually reads: a blurb, an intelligence note, the
        /// remark on a man's file. Serif, because the design sets every one of them in
        /// one.</summary>
        public static TextMeshProUGUI Copytext(Transform parent, float x, float y, float w,
            float h, string text, float size = 13.5f, Color? colour = null,
            bool italic = false)
        {
            var copy = Paragraph(parent, italic ? LedgerStyle.SerifItalic : LedgerStyle.Serif,
                size, colour ?? Copy, x, y, w, h, text, lineSpacing: 3f);
            copy.overflowMode = TextOverflowModes.Ellipsis;
            return copy;
        }

        // ------------------------------------------------------------------- marks

        /// <summary>A dotted leader between a label and the figure that answers it.</summary>
        public static void Leader(Transform parent, float x, float y, float w) =>
            DottedRule(parent, x, y, w, Dotted);

        /// <summary>An unfilled pip. The design gives it a colour of its own rather
        /// than a faded copy of the fill: a wash of the fill at low alpha disappears on
        /// a dark rail, and a row of pips that cannot be counted is a bar.</summary>
        public static readonly Color PipEmpty = Rgb(0x48413c);

        /// <summary>
        /// A run of hard square pips - a reading with a ceiling. The design's meter is a
        /// row of blocks, never a bar with a rounded end.
        ///
        /// The measurements are QUANTISED to the canvas scale first. The ledger canvas
        /// runs at a fractional scale, and independent quads at the same authored width
        /// otherwise rasterise one physical pixel apart - a run of pips comes out with
        /// its blocks visibly uneven. Quantising the width, the height and the pitch
        /// once makes every mark in the run land on the same grid.
        /// </summary>
        public static void Pips(Transform parent, float x, float centreY, int total,
            int filled, Color colour, float w = 9f, float h = 9f, float pitch = 11f,
            Color? empty = null)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            var scale = canvas ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            w = Mathf.Max(1f / scale, Mathf.Round(w * scale) / scale);
            h = Mathf.Max(1f / scale, Mathf.Round(h * scale) / scale);
            pitch = Mathf.Max(w, Mathf.Round(pitch * scale) / scale);

            var unlit = empty ?? PipEmpty;
            for (var i = 0; i < total; i++)
            {
                var pip = NewRect("Pip", parent);
                pip.anchorMin = pip.anchorMax = new Vector2(0f, 1f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.anchoredPosition = new Vector2(x + i * pitch, centreY);
                pip.sizeDelta = new Vector2(w, h);
                Fill(pip, i < filled ? colour : unlit);
            }
        }

        public static float PipsWidth(int total, float w = 9f, float pitch = 11f) =>
            total <= 0 ? 0f : (total - 1) * pitch + w;

        /// <summary>
        /// A capacity meter: the label, the figure it comes to, the trough with its
        /// fill, and the line of plain English under it that says what the figure MEANS.
        /// The last is the point - a ratio nobody can act on is not a readout.
        /// Answers the height it took.
        /// </summary>
        public static float Meter(Transform parent, float x, float y, float w,
            string label, int current, int maximum, string unit, string plural,
            bool dark = false)
        {
            var over = maximum > 0 && current > maximum;
            var full = maximum > 0 && current >= maximum;
            var colour = over ? (dark ? Boss : Red)
                : full ? Amber
                : (dark ? HeadCream : Ink);

            var text = Mono(parent, x, y, w - 90f, label, 10f,
                dark ? HeadDim : Muted, 6f);
            text.overflowMode = TextOverflowModes.Ellipsis;
            Figure(parent, x + w - 90f, y - 1f, 90f, current + " / " + maximum, 14f, colour);

            var trough = NewRect("Trough", parent);
            PlaceTopLeft(trough, x, y - 20f, w, 7f);
            Fill(trough, dark ? LedgerStyle.RailTrough : Trough);
            var fraction = maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0f;
            var ink = NewRect("Fill", trough);
            PlaceTopLeft(ink, 0f, 0f, w * fraction, 7f);
            Fill(ink, colour);

            var room = maximum - current;
            var note = over
                ? "OVER BY " + (current - maximum) + " · the outfit will not add more"
                : current == maximum
                    ? "at the limit · no room for another " + unit
                    : room + " more " + (room == 1 ? unit : plural) + " will fit";
            var line = Mono(parent, x, y - 30f, w, note, 10f,
                over ? (dark ? Boss : Red) : (dark ? HeadDim : Muted), 1f);
            if (over)
                line.font = LedgerStyle.MonoBold;
            return 46f;
        }

        // ----------------------------------------------------------------- buttons

        /// <summary>How a button reads. FILED is the design's dark key with the hard
        /// shadow under it, OUTLINE is a hairline box, GHOST is bare type - and RED is
        /// the key that undoes something.</summary>
        public enum Key
        {
            Dark,
            Outline,
            Ghost,
            Red,
        }

        /// <summary>
        /// A key: flat, hard-edged, with the design's drop shadow under the filled
        /// ones. Nothing rounds, nothing gradients, and nothing pretends to be tape
        /// stuck to a page.
        /// </summary>
        public static TextMeshProUGUI Button(Transform parent, string label, float x,
            float y, float w, float h, UnityAction onClick, Key key = Key.Outline,
            float size = 10.5f)
        {
            var rect = NewRect("Key " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var filled = key == Key.Dark || key == Key.Red;
            if (filled)
                Shadow(rect, "Key shadow", 5f, new Vector2(0f, -2f), 0.30f);

            var face = rect.gameObject.AddComponent<Image>();
            face.sprite = null;
            face.color = key switch
            {
                Key.Dark => Head,
                Key.Red => Red,
                Key.Outline => new Color(Panel.r, Panel.g, Panel.b, 0f),
                _ => new Color(Panel.r, Panel.g, Panel.b, 0f),
            };
            face.raycastTarget = true;
            if (key == Key.Outline)
                Frame(rect, 1f, SheetRule);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = filled
                ? new Color(1.3f, 1.3f, 1.3f)
                : new Color(0.9f, 0.88f, 0.86f);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colours.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text("Label", rect, LedgerStyle.MonoBold, size,
                filled ? HeadCream : (key == Key.Ghost ? Red : Ink),
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 7f;
            text.text = label.ToUpperInvariant();
            return text;
        }

        /// <summary>
        /// The same key, asked for the way the first edition's pages ask: a red flag for
        /// the verb that undoes something and an outline flag for the one that does not
        /// commit. Kept so every page can name a key by what it DOES rather than by
        /// which of the four faces it happens to wear.
        /// </summary>
        public static TextMeshProUGUI Button(Transform parent, string label, float x,
            float y, float w, float h, UnityAction onClick, bool red, float size = 10.5f,
            bool outline = false) =>
            Button(parent, label, x, y, w, h, onClick,
                red ? (outline ? Key.Ghost : Key.Red) : outline ? Key.Outline : Key.Dark,
                size);

        /// <summary>
        /// Greys a key that cannot be pressed rather than taking it off the panel: a row
        /// that has vanished tells the reader nothing about why.
        ///
        /// <paramref name="dead"/> is the word's colour when it cannot be pressed and
        /// <paramref name="deadFrame"/> its hairline's. A panel painted DARK has to name
        /// both: the paper greys are BRIGHT over a near-black fill, so a dead key left in
        /// them reads exactly as live as the one beside it, and the whole row of keys
        /// then reads as neither.
        /// </summary>
        public static void KeyEnabled(TMP_Text label, bool enabled, Color? dead = null,
            Color? deadFrame = null)
        {
            if (!label)
                return;
            var key = label.GetComponentInParent<Button>();
            if (key)
                key.interactable = enabled;
            if (enabled)
                return;
            label.color = dead ?? Rule;
            if (deadFrame.HasValue)
                KeyFrame(label, deadFrame.Value);
        }

        /// <summary>
        /// Recolours a key's hairline box. The outline key is drawn for PAPER - warm grey
        /// rule round dark ink on a cream sheet - and that same hairline over the dark
        /// head fill is all but gone, which leaves a live key looking like a word somebody
        /// left lying on the panel. A surface painted dark says what colour its boxes are
        /// drawn in; nothing else about the key changes.
        /// </summary>
        public static void KeyFrame(TMP_Text label, Color colour)
        {
            if (!label)
                return;
            var key = label.GetComponentInParent<Button>();
            if (!key)
                return;
            var edges = key.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < edges.Length; i++)
                if (edges[i].gameObject.name == "Edge")
                    edges[i].color = colour;
        }

        /// <summary>The width a key needs for its word at the design's padding.</summary>
        public static float ButtonWidth(string label, float size = 10.5f,
            float spacing = 7f, float pad = 15f) =>
            pad * 2f + label.Length * (size * 0.6f + size * spacing / 100f);

        /// <summary>One of a single-select run - a sort order, a shelf, a filter. The
        /// chosen one is the dark key; the rest are hairline boxes.</summary>
        public static TextMeshProUGUI Chip(Transform parent, string label, float x,
            float y, float w, float h, bool active, UnityAction onClick,
            float size = 10.5f) =>
            Button(parent, label, x, y, w, h, onClick,
                active ? Key.Dark : Key.Outline, size);

        /// <summary>
        /// A segmented run: one question with one answer, drawn as a single hairline
        /// box divided into butted cells with the chosen cell struck dark. What the
        /// design uses where a row of separate keys would read as a toolbar - separate
        /// keys say "here are four things you may press", a segmented bar says "the
        /// sheet is showing this one of these three". Answers the width it took, so a
        /// caller laying right to left can step back past it.
        /// </summary>
        public static float Segmented(Transform parent, float x, float y, float h,
            string[] labels, int active, System.Action<int> pick, float cellW = 0f,
            float size = 9.5f)
        {
            if (labels == null || labels.Length == 0)
                return 0f;

            var w = cellW;
            if (w <= 0f)
                for (var i = 0; i < labels.Length; i++)
                    w = Mathf.Max(w, ButtonWidth(labels[i], size, 5f, 13f));

            var bar = NewRect("Segmented", parent);
            PlaceTopLeft(bar, x, y, w * labels.Length, h);

            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var cell = NewRect("Segment " + labels[i], bar);
                PlaceTopLeft(cell, i * w, 0f, w, h);

                var face = cell.gameObject.AddComponent<Image>();
                face.color = i == active
                    ? Head
                    : new Color(Panel.r, Panel.g, Panel.b, 0f);
                face.raycastTarget = true;

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                var colours = button.colors;
                colours.normalColor = Color.white;
                colours.highlightedColor = i == active
                    ? new Color(1.3f, 1.3f, 1.3f)
                    : new Color(0.9f, 0.88f, 0.86f);
                colours.selectedColor = colours.highlightedColor;
                colours.pressedColor = new Color(0.72f, 0.72f, 0.72f);
                button.colors = colours;
                if (pick != null)
                    button.onClick.AddListener(() => pick(index));

                var label = Text("Label", cell, LedgerStyle.MonoBold, size,
                    i == active ? HeadCream : Ink, TextAlignmentOptions.Center);
                Stretch(label.rectTransform, 4f);
                label.characterSpacing = 5f;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.text = labels[i].ToUpperInvariant();
            }

            // The dividers and the box go on LAST: a dark cell laid over them would
            // otherwise break the bar's outline at whichever segment is chosen.
            for (var i = 1; i < labels.Length; i++)
                Block("Divider", bar, i * w, 0f, 1f, h, SheetRule);
            Frame(bar, 1f, SheetRule);
            return w * labels.Length;
        }

        /// <summary>The Button behind a key's label, for a caller that wants to disable
        /// it.</summary>
        public static Button KeyOf(TextMeshProUGUI label) =>
            label.transform.parent.GetComponent<Button>();

        /// <summary>
        /// A status chip: a filled block with the word set in cream mono caps. What the
        /// v2 sheet says instead of a rubber stamp - the first edition tilted a word in
        /// red ink across a photograph, and a terminal does not own a stamp.
        /// </summary>
        public static TextMeshProUGUI Status(Transform parent, float x, float y, float w,
            float h, string word, Color background, float size = 10.5f)
        {
            var rect = NewRect("Status " + word, parent);
            PlaceTopLeft(rect, x, y, w, h);
            Fill(rect, background);
            var text = Text("Label", rect, LedgerStyle.MonoBold, size, HeadCream,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 6f;
            text.text = word.ToUpperInvariant();
            return text;
        }

        // ------------------------------------------------------------------ plates

        /// <summary>
        /// A flat plate: the ground a photograph, a catalogue cut or a portrait sits
        /// on, printed with its own initials until the studio hands over a picture.
        /// Answers the RawImage the portrait studio paints into.
        /// </summary>
        public static RawImage PortraitPlate(Transform parent, float x, float y, float w,
            float h, string initials, Color? face = null, Color? ink = null)
        {
            var rect = NewRect("Plate", parent);
            PlaceTopLeft(rect, x, y, w, h);
            Fill(rect, face ?? Portrait);

            var mark = Caps(rect, 0f, -(h - 14f) * 0.5f, w, initials, 11f,
                ink ?? Muted, 8f, TextAlignmentOptions.Center);
            mark.font = LedgerStyle.Mono;

            // WHITE and disabled, never transparent: PortraitStudio.Show sets the
            // texture and enables the image but never touches its colour, so a plate
            // built at zero alpha stays empty forever however well the studio renders.
            var picture = NewRect("Picture", rect);
            Stretch(picture);
            var raw = picture.gameObject.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            raw.enabled = false;
            return raw;
        }

        /// <summary>A hatched square - the mark the design puts beside a name that is
        /// on PAPER, as against the solid square beside one that is true on the
        /// street.</summary>
        public static void PaperMark(Transform parent, float x, float y, Color colour,
            float size = 12f)
        {
            var rect = NewRect("Paper mark", parent);
            PlaceTopLeft(rect, x, y, size, size);
            Frame(rect, 2f, colour);
            Texture(rect, LedgerStyle.Hatch, colour, size, size, 4f);
        }

        /// <summary>A solid square - what the street says, as against what the paper
        /// does.</summary>
        public static void StreetMark(Transform parent, float x, float y, Color colour,
            float size = 12f) =>
            Block("Street mark", parent, x, y, size, size, colour);
    }
}
