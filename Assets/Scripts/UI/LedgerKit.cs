using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's stationery drawer: every object the book lays on its pages, built
    /// from primitives at runtime. Typed lines, ruled and dotted rules, greenbar
    /// banding, punched holes, telex slips, paperclips, hatched plates where the art
    /// goes, stepped meters, index cards with a shadow, sticky notes, gold stars.
    /// Coordinates are page coordinates everywhere - x right, y DOWN as a negative
    /// anchoredPosition from the parent's top-left - the one convention every page
    /// shares.
    ///
    /// Only click surfaces are raycast targets; every decoration says so explicitly,
    /// because a stray raycast target on the paper eats a click meant for the row under it.
    /// </summary>
    public static class LedgerKit
    {
        // -------------------------------------------------------------- rect basics

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Top-left anchored placement in page coordinates.</summary>
        public static void PlaceTopLeft(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>A cell spanning a row's full height at the given x.</summary>
        public static void FillRow(RectTransform rect, float x, float w)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(w, 0f);
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // ------------------------------------------------------------------ fills

        /// <summary>A flat fill, never a raycast target.</summary>
        public static Image Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Stock with a fall in it: the flat colour, then the darker stop laid
        /// over it through a shared transparent-to-opaque column. Every sheet in the
        /// design is a gradient, and this is what one costs.</summary>
        public static Image Stock(RectTransform rect, Color top, Color low)
        {
            var image = Fill(rect, top);
            var fade = NewRect("Fall", rect);
            Stretch(fade);
            var raw = fade.gameObject.AddComponent<RawImage>();
            raw.texture = LedgerStyle.FadeUp;
            raw.color = low;
            raw.raycastTarget = false;
            return image;
        }

        /// <summary>A pre-baked three-stop fall - the desk and the ledger's own sheet.</summary>
        public static RawImage Gradient(RectTransform rect, Texture2D fall)
        {
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = fall;
            raw.color = Color.white;
            raw.raycastTarget = false;
            return raw;
        }

        public static Image Block(string name, Transform parent, float x, float y, float w,
            float h, Color color)
        {
            var rect = NewRect(name, parent);
            PlaceTopLeft(rect, x, y, w, h);
            return Fill(rect, color);
        }

        /// <summary>A hairline rule across w units at (x, y).</summary>
        public static Image Rule(Transform parent, float x, float y, float w, Color color,
            float thickness = 1f) =>
            Block("Rule", parent, x, y, w, thickness, color);

        /// <summary>A vertical rule h units tall from (x, y) downwards.</summary>
        public static Image VRule(Transform parent, float x, float y, float h, Color color,
            float thickness = 1f) =>
            Block("VRule", parent, x, y, thickness, h, color);

        /// <summary>A dotted leader - two units on, two off - between a label and the
        /// figure that answers it. One tiled quad, not a hundred squares.</summary>
        public static void DottedRule(Transform parent, float x, float y, float w, Color color)
        {
            var rect = NewRect("Dotted", parent);
            PlaceTopLeft(rect, x, y, w, 1f);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = LedgerStyle.DotRule;
            raw.color = color;
            raw.uvRect = new Rect(0f, 0f, w / 4f, 1f);
            raw.raycastTarget = false;
        }

        /// <summary>The design's heading rule: 2 units of ink over a 1-unit ghost of
        /// it. Every section on the sheet is closed with this pair.</summary>
        public static void DoubleRule(Transform parent, float x, float y, float w, Color color)
        {
            Rule(parent, x, y, w, color, 2f);
            var ghost = color;
            ghost.a *= 0.5f;
            Rule(parent, x, y - 4f, w, ghost);
        }

        /// <summary>Four hairlines round a rect - a typed box, the pen-drawn frame.</summary>
        public static void Frame(RectTransform rect, float thickness, Color color)
        {
            Edge(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), thickness, color);
            Edge(rect, new Vector2(0f, 0f), new Vector2(0f, 1f), thickness, color);
            Edge(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), thickness, color);
            Edge(rect, new Vector2(1f, 0f), new Vector2(1f, 1f), thickness, color);
        }

        static void Edge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            float thickness, Color color)
        {
            var edge = NewRect("Edge", parent);
            edge.anchorMin = anchorMin;
            edge.anchorMax = anchorMax;
            var horizontal = anchorMin.y == anchorMax.y;
            edge.pivot = new Vector2(anchorMin.x, anchorMin.y);
            edge.anchoredPosition = Vector2.zero;
            edge.sizeDelta = horizontal
                ? new Vector2(0f, thickness)
                : new Vector2(thickness, 0f);
            Fill(edge, color);
        }

        /// <summary>The paper grain laid over a sheet - a tiling dark speckle at low
        /// alpha. Sized to the sheet so the tile never stretches.</summary>
        public static void Grain(RectTransform sheet, float w, float h, float strength = 1f)
        {
            var rect = NewRect("Grain", sheet);
            Stretch(rect);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = LedgerStyle.PaperGrain;
            raw.color = new Color(1f, 1f, 1f, strength);
            raw.uvRect = new Rect(0f, 0f, w / 256f, h / 256f);
            raw.raycastTarget = false;
        }

        /// <summary>A tiling texture laid over a rect - the desk's stripe, a hatch.</summary>
        public static RawImage Texture(RectTransform rect, Texture2D texture, Color tint,
            float w, float h, float scale)
        {
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.color = tint;
            raw.uvRect = new Rect(0f, 0f, w / scale, h / scale);
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>The soft shadow under a sheet - a 9-sliced blur laid 4 units down
        /// and right, drawn BEFORE the sheet so it sits under it.</summary>
        public static void ShadowUnder(RectTransform sheet, float spread = 10f, Color? tint = null)
        {
            var rect = NewRect("Shadow", sheet.parent);
            rect.SetSiblingIndex(sheet.GetSiblingIndex());
            rect.anchorMin = sheet.anchorMin;
            rect.anchorMax = sheet.anchorMax;
            rect.pivot = sheet.pivot;
            // Grown by spread on every side about the SHEET's centre, whatever the
            // pivot, then nudged down-right the way a lamp up-left throws it.
            rect.anchoredPosition = sheet.anchoredPosition + new Vector2(4f, -5f) +
                new Vector2(spread * (2f * sheet.pivot.x - 1f),
                    spread * (2f * sheet.pivot.y - 1f));
            rect.sizeDelta = sheet.sizeDelta + new Vector2(spread * 2f, spread * 2f);
            rect.localRotation = sheet.localRotation;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.SoftShadow;
            image.type = Image.Type.Sliced;
            image.color = tint ?? LedgerStyle.Shadow;
            image.raycastTarget = false;
        }

        /// <summary>A card of stock laid on the page: shadow, fill, grain - all its own
        /// children, so a card that hides, moves or is destroyed takes its shadow with
        /// it. Content goes on the returned rect in its own page coordinates; the
        /// paper's Image is the second child, "Paper", for a caller that wants the
        /// card to catch clicks.</summary>
        public static RectTransform Card(string name, Transform parent, float x, float y,
            float w, float h, Color stock, float tiltDegrees = 0f, float shadowSpread = 10f,
            Color? low = null)
        {
            var rect = NewRect(name, parent);
            PlaceTopLeft(rect, x, y, w, h);
            if (tiltDegrees != 0f)
                rect.localRotation = Quaternion.Euler(0f, 0f, tiltDegrees);

            var shadow = NewRect("Shadow", rect);
            shadow.anchorMin = Vector2.zero;
            shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-shadowSpread + 4f, -shadowSpread - 5f);
            shadow.offsetMax = new Vector2(shadowSpread + 4f, shadowSpread - 5f);
            var image = shadow.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.SoftShadow;
            image.type = Image.Type.Sliced;
            image.color = LedgerStyle.Shadow;
            image.raycastTarget = false;

            var paper = NewRect("Paper", rect);
            Stretch(paper);
            if (low.HasValue)
                Stock(paper, stock, low.Value);
            else
                Fill(paper, stock);
            Grain(paper, w, h, 0.8f);
            return rect;
        }

        /// <summary>The paper Image of a Card - the surface to make a raycast target.</summary>
        public static Image PaperOf(RectTransform card) =>
            card.Find("Paper").GetComponent<Image>();

        // ------------------------------------------------------------------- text

        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font,
            float size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font)
                text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A placed single line.</summary>
        public static TextMeshProUGUI Line(Transform parent, TMP_FontAsset font, float size,
            Color color, float x, float y, float w, float h, string content,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = Text("Text", parent, font, size, color, alignment);
            PlaceTopLeft(text.rectTransform, x, y, w, h);
            text.text = content;
            return text;
        }

        /// <summary>
        /// The book's one voice for a label: letter-spaced small caps in the condensed
        /// gothic. Every LABEL / VALUE pair, every column head, every kicker on the
        /// sheet is one of these - the design puts .1em to .22em on all of them, and
        /// this is where that lives instead of at four hundred call sites.
        /// </summary>
        public static TextMeshProUGUI Caps(Transform parent, float x, float y, float w,
            string label, float size = 10f, Color? color = null, float spacing = 4f,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = Line(parent, LedgerStyle.Condensed, size,
                color ?? LedgerStyle.InkLabel, x, y, w, size + 6f,
                label.ToUpperInvariant(), alignment);
            text.characterSpacing = spacing;
            return text;
        }

        /// <summary>
        /// The height one line of a face needs at a size. TMP with Ellipsis drops a
        /// line WHOLE when the rect cannot hold it, so a condensed gothic - whose line
        /// box runs about half again its point size - silently prints nothing in a rect
        /// sized to the point size. Every truncating line is measured through this.
        /// </summary>
        public static float LineBox(float size, int lines = 1) => size * 1.55f * lines + 4f;

        /// <summary>A wrapping block of copy.</summary>
        public static TextMeshProUGUI Paragraph(Transform parent, TMP_FontAsset font,
            float size, Color color, float x, float y, float w, float h, string content,
            float lineSpacing = 4f)
        {
            var text = Text("Paragraph", parent, font, size, color,
                TextAlignmentOptions.TopLeft);
            PlaceTopLeft(text.rectTransform, x, y, w, h);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            text.lineSpacing = lineSpacing;
            text.text = content;
            return text;
        }

        /// <summary>A typed heading with the rule a typist strikes under it. Returns
        /// the y below the rule.</summary>
        public static float Heading(Transform parent, float x, float y, float w, string label,
            float size = 15f, bool doubleRule = false)
        {
            var text = Line(parent, LedgerStyle.Condensed, size, LedgerStyle.Ink, x, y, w,
                size + 10f, label.ToUpperInvariant());
            text.characterSpacing = 5f;
            var ruleY = y - size - 12f;
            if (doubleRule)
                DoubleRule(parent, x, ruleY, w, LedgerStyle.Ink);
            else
                Rule(parent, x, ruleY, w, LedgerStyle.InkFaint);
            return ruleY - 10f;
        }

        // ------------------------------------------------- the file's own furniture

        /// <summary>
        /// The punched holes down an edge of the sheet: a column of dark discs at a
        /// fixed pitch, the way a sheet torn out of a ring binder reads. Drawn on the
        /// sheet itself so they scroll with nothing and clip with the paper.
        /// </summary>
        public static void PunchStrip(Transform parent, float centreX, float top, float height,
            float radius = 5f, float pitch = 30f)
        {
            var strip = NewRect("Punches", parent);
            PlaceTopLeft(strip, centreX - radius, top, radius * 2f, height);
            // Half a pitch of inset so the first hole is not flush with the top edge.
            for (var y = pitch * 0.5f; y <= height - radius; y += pitch)
            {
                var hole = NewRect("Hole", strip);
                PlaceTopLeft(hole, 0f, -(y - radius), radius * 2f, radius * 2f);
                var image = hole.gameObject.AddComponent<Image>();
                image.sprite = LedgerStyle.Disc;
                image.color = LedgerStyle.Punch;
                image.raycastTarget = false;
            }
        }

        /// <summary>The ring a cup left on the corner of the file. Pure decoration and
        /// the first thing to switch off if the page ever feels busy.</summary>
        public static void CoffeeStain(Transform parent, float x, float y, float diameter,
            float tilt = -6f)
        {
            var rect = NewRect("Coffee Ring", parent);
            PlaceTopLeft(rect, x, y, diameter, diameter);
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.Ring;
            image.color = LedgerStyle.CoffeeRing;
            image.raycastTarget = false;
        }

        /// <summary>
        /// A telex slip: cream stock, a red rule down its left edge, the source and the
        /// time in small caps across the head, the message under. What came in over the
        /// night - never a thing to press.
        /// </summary>
        public static RectTransform Slip(Transform parent, float x, float y, float w, float h,
            string source, string time, string body)
        {
            var rect = NewRect("Slip", parent);
            PlaceTopLeft(rect, x, y, w, h);
            Stock(rect, LedgerStyle.Slip, LedgerStyle.SlipLow);
            Grain(rect, w, h, 0.6f);

            var edge = NewRect("Edge", rect);
            PlaceTopLeft(edge, 0f, 0f, 3f, h);
            Fill(edge, new Color(LedgerStyle.RedPen.r, LedgerStyle.RedPen.g,
                LedgerStyle.RedPen.b, 0.6f));

            Caps(rect, 12f, -6f, w - 80f, source, 9f, LedgerStyle.InkLabel, 3.5f);
            Caps(rect, w - 74f, -6f, 62f, time, 9f, LedgerStyle.InkLabel, 2f,
                TextAlignmentOptions.MidlineRight);

            var copy = Paragraph(rect, LedgerStyle.Mono, 12f, LedgerStyle.InkSoft, 12f, -24f,
                w - 24f, h - 28f, body, lineSpacing: 2f);
            copy.overflowMode = TextOverflowModes.Ellipsis;
            return rect;
        }

        /// <summary>The paperclip straddling the dossier's top edge: an open steel
        /// rectangle, drawn half above the card. Purely a sign that the card is a
        /// physical thing somebody clipped shut.</summary>
        public static void Clip(Transform parent, float centreX, float topY,
            float w = 58f, float h = 26f)
        {
            var rect = NewRect("Paperclip", parent);
            PlaceTopLeft(rect, centreX - w * 0.5f, topY + h * 0.5f, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.RoundedSmall;
            image.type = Image.Type.Sliced;
            image.color = Color.clear;
            image.raycastTarget = false;
            Frame(rect, 3f, LedgerStyle.Paperclip);
        }

        /// <summary>
        /// A printer's plate: the hatched block-out that stands where a picture goes,
        /// with its part number under it in small caps. Returns the RawImage the
        /// portrait studio prints into - disabled until a print lands, so the hatch is
        /// both the placeholder AND the honest answer when no model resolves.
        /// </summary>
        public static RawImage Plate(Transform parent, float x, float y, float w, float h,
            string caption, Color? tint = null)
        {
            var frame = NewRect("Plate", parent);
            PlaceTopLeft(frame, x, y, w, h);
            Fill(frame, tint ?? LedgerStyle.PolaroidDark);

            var hatch = NewRect("Hatch", frame);
            Stretch(hatch);
            Texture(hatch, LedgerStyle.Hatch, Color.white, w, h, 20f);

            Frame(frame, 1f, LedgerStyle.InkFaint);

            if (caption.Length > 0)
                Caps(frame, 0f, -(h - 16f), w, caption, 8.5f, LedgerStyle.InkLabel, 2f,
                    TextAlignmentOptions.Center);

            var print = NewRect("Print", frame);
            Stretch(print, 1f);
            var raw = print.gameObject.AddComponent<RawImage>();
            raw.color = LedgerStyle.PhotoTint;
            raw.raycastTarget = false;
            raw.enabled = false;
            return raw;
        }

        /// <summary>Greenbar banding: the pale green stripe an accounting sheet prints
        /// every other line, so the eye tracks a figure across the page. Drawn under
        /// the rows, at the row pitch, never at a pitch of its own.</summary>
        public static void Greenbar(Transform parent, float x, float y, float w, float h,
            float pitch)
        {
            var root = NewRect("Bands", parent);
            PlaceTopLeft(root, x, y, w, h);
            root.gameObject.AddComponent<RectMask2D>();
            var band = 0;
            for (var top = 0f; top < h; top += pitch, band++)
                if (band % 2 == 1)
                    Block("Band", root, 0f, -top, w, Mathf.Min(pitch, h - top),
                        LedgerStyle.GreenbarBand);
        }

        /// <summary>The torn perforated edge of a till roll: half-discs in the colour
        /// of whatever is BEHIND the tape, bitten out of its bottom edge.</summary>
        public static void Perforation(Transform parent, float x, float y, float w,
            Color behind, float pitch = 12f)
        {
            var root = NewRect("Perforation", parent);
            PlaceTopLeft(root, x, y, w, pitch);
            for (var cx = pitch * 0.5f; cx < w; cx += pitch)
            {
                var tooth = NewRect("Tooth", root);
                PlaceTopLeft(tooth, cx - pitch * 0.5f, 0f, pitch, pitch);
                var image = tooth.gameObject.AddComponent<Image>();
                image.sprite = LedgerStyle.Disc;
                image.color = behind;
                image.raycastTarget = false;
            }
        }

        // ------------------------------------------------------- highlighter & marks

        /// <summary>The selected row: a wash of red across it and a heavy rule down its
        /// left edge - a clerk's tick, not a UI selection.</summary>
        public static Image Highlight(RectTransform row, Color color, float inset = 0f)
        {
            var rect = NewRect("Highlight", row);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
            rect.SetAsFirstSibling();
            var wash = Fill(rect, color);

            var edge = NewRect("Edge", rect);
            edge.anchorMin = new Vector2(0f, 0f);
            edge.anchorMax = new Vector2(0f, 1f);
            edge.pivot = new Vector2(0f, 0.5f);
            edge.anchoredPosition = Vector2.zero;
            edge.sizeDelta = new Vector2(3f, 0f);
            // The rule is the same hue at full strength - the wash says WHICH, the
            // rule says WHERE, and a wash alone is invisible on a busy sheet.
            var solid = color;
            solid.a = 1f;
            Fill(edge, solid);
            return wash;
        }

        /// <summary>A pen ring round something - the circled choice.</summary>
        public static void PenRing(RectTransform around, Color color)
        {
            var rect = NewRect("Ring", around);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-4f, -4f);
            rect.offsetMax = new Vector2(4f, 4f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.Rounded;
            image.type = Image.Type.Sliced;
            image.color = Color.clear;
            image.raycastTarget = false;
            Frame(rect, 2f, color);
        }

        // ------------------------------------------------------------- the buttons

        /// <summary>
        /// An ACTION button, the design's dark chip: square, ink-black (or red for the
        /// verb that commits), cream condensed caps, a hard 2-unit edge under it the
        /// way a key on a desk toy sits proud. Outlined is the same button with the
        /// fill taken away - what the sheet uses for a verb that UNDOES something.
        ///
        /// Kept under the old name because every page in the book calls it: what the
        /// 1987 redesign changed is the face, not the part it plays.
        /// </summary>
        public static TextMeshProUGUI Tape(Transform parent, string label, float x, float y,
            float w, float h, UnityAction onClick, bool red = false, float size = 12f,
            bool outline = false)
        {
            var rect = NewRect("Tape " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var ink = red ? LedgerStyle.TapeRed : LedgerStyle.TapeBlack;

            var strip = rect.gameObject.AddComponent<Image>();
            strip.sprite = null;
            strip.color = outline ? new Color(ink.r, ink.g, ink.b, 0f) : ink;
            strip.raycastTarget = true;

            if (outline)
                Frame(rect, 1f, ink);
            else
                Block("Edge", rect, 0f, -h, w, 2f, new Color(0f, 0f, 0f, 0.35f));

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = strip;
            var colours = button.colors;
            colours.normalColor = outline ? Color.white : LedgerStyle.TapeNormal;
            colours.highlightedColor = outline
                ? new Color(1f, 1f, 1f, 1f) : LedgerStyle.TapeHover;
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = LedgerStyle.TapePressed;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text("Label", rect, LedgerStyle.Condensed, size,
                outline ? ink : LedgerStyle.TapeText, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 6f;
            text.text = label.ToUpperInvariant();
            return text;
        }

        /// <summary>
        /// A toolbar pill: one of a single-select run - a sort order, an armory shelf.
        /// The chosen one is filled with ink and printed in cream; the rest are a wash
        /// of ink under a hairline. Nothing here commits anything, which is exactly why
        /// it must not look like an action button.
        /// </summary>
        public static TextMeshProUGUI Pill(Transform parent, string label, float x, float y,
            float w, float h, bool active, UnityAction onClick, float size = 10.5f)
        {
            var rect = NewRect("Pill " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var face = rect.gameObject.AddComponent<Image>();
            face.sprite = null;
            face.color = active ? LedgerStyle.TapeBlack : LedgerStyle.TapeIdle;
            face.raycastTarget = true;
            if (!active)
                Frame(rect, 1f, LedgerStyle.InkFaint);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colours.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text("Label", rect, LedgerStyle.Condensed, size,
                active ? LedgerStyle.TapeText : LedgerStyle.InkMid,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 4f;
            text.text = label.ToUpperInvariant();
            return text;
        }

        /// <summary>The tape's Button, for a caller that wants to disable it.</summary>
        public static Button ButtonOf(TextMeshProUGUI tapeLabel) =>
            tapeLabel.transform.parent.GetComponent<Button>();

        /// <summary>An invisible Button over a row whose Image doubles as its target
        /// graphic - the whole row is the click surface.</summary>
        public static void RowButton(RectTransform rect, Image background, UnityAction onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
        }

        /// <summary>A clickable transparent surface over a rect.</summary>
        public static Image ClickSurface(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = Color.clear;
            image.raycastTarget = true;
            return image;
        }

        // ------------------------------------------------------------ rubber stamp

        /// <summary>A rubber stamp: red condensed caps in a double frame, tilted, never
        /// quite opaque. Laid over whatever it judges.</summary>
        public static RectTransform Stamp(Transform parent, string word, float x, float y,
            float w, float h, float tilt = -7f, float size = 20f)
        {
            var rect = NewRect("Stamp " + word, parent);
            PlaceTopLeft(rect, x, y, w, h);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x + w * 0.5f, y - h * 0.5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Frame(rect, 2f, LedgerStyle.StampRed);
            var inner = NewRect("Inner", rect);
            Stretch(inner, 3f);
            Frame(inner, 1f, LedgerStyle.StampRed);
            var text = Text("Word", rect, LedgerStyle.Condensed, size, LedgerStyle.StampRed,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 8f;
            text.text = word.ToUpperInvariant();
            return rect;
        }

        // ---------------------------------------------------------------- polaroid

        /// <summary>
        /// A Polaroid: white border, the print inside, a wider strip at the bottom for
        /// the caption. The initials are the placeholder AND the fallback - the print
        /// covers them when PortraitStudio lands it, and when no model resolves they
        /// simply stay. Returns the RawImage the studio prints into (disabled - Show
        /// flips it on).
        /// </summary>
        public static RawImage Polaroid(Transform parent, float x, float y, float photoSize,
            string initials, float tilt, out RectTransform frame, string caption = "")
        {
            const float border = 7f;
            const float lip = 22f;
            var w = photoSize + border * 2f;
            var h = photoSize + border + lip;

            frame = NewRect("Polaroid", parent);
            PlaceTopLeft(frame, x, y, w, h);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = new Vector2(x + w * 0.5f, y - h * 0.5f);
            frame.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Fill(frame, LedgerStyle.PolaroidWhite);
            ShadowUnder(frame, 8f);

            var photo = NewRect("Photo", frame);
            PlaceTopLeft(photo, border, -border, photoSize, photoSize);
            Fill(photo, LedgerStyle.PolaroidDark);
            var hatch = NewRect("Hatch", photo);
            Stretch(hatch);
            Texture(hatch, LedgerStyle.Hatch, Color.white, photoSize, photoSize, 20f);

            var text = Text("Initials", photo, LedgerStyle.Condensed, photoSize * 0.34f,
                LedgerStyle.InkLabel, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.text = initials;

            var print = NewRect("Print", photo);
            Stretch(print);
            var raw = print.gameObject.AddComponent<RawImage>();
            raw.color = LedgerStyle.PhotoTint;
            raw.raycastTarget = false;
            raw.enabled = false;

            if (caption.Length > 0)
                Caps(frame, 0f, -(border + photoSize + 3f), w, caption, 8.5f,
                    LedgerStyle.InkLabel, 2f, TextAlignmentOptions.Center);

            return raw;
        }

        // ------------------------------------------------------------------ meters

        /// <summary>
        /// Five gold stars - UiSkin's baked family (full / half / a pen-outlined
        /// empty), the star stickers a 1987 personnel form gets. Centred on centreY,
        /// starting at x.
        /// </summary>
        public static void Stars(Transform parent, float x, float centreY, int halfSteps,
            float size = 19f, float pitch = 21f)
        {
            for (var slot = 0; slot < 5; slot++)
            {
                var rect = NewRect("Star", parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x + slot * pitch, centreY);
                rect.sizeDelta = new Vector2(size, size);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = halfSteps >= (slot + 1) * 2 ? UiSkin.StarFull
                    : halfSteps == slot * 2 + 1 ? UiSkin.StarHalf
                    : UiSkin.StarEmpty;
                image.color = Color.white;
                image.raycastTarget = false;
            }
        }

        /// <summary>
        /// The design's meter: a run of typed blocks, so many struck and the rest
        /// hollow. It is a COUNT, never a percentage - six of ten reads as six marks
        /// and not as a bar that happens to stop somewhere.
        ///
        /// Drawn as rects rather than the handoff's typed glyphs on purpose: not one
        /// face in Assets/Fonts/Ledger1987 cuts U+25AE or U+25AF, so a typed bar would
        /// print a row of tofu. Same measurement, same rhythm, letters the game owns.
        /// </summary>
        public static void StepBar(Transform parent, float x, float centreY, int steps,
            int filled, Color fill, float blockW = 5f, float blockH = 11f, float pitch = 7f)
        {
            var empty = new Color(fill.r, fill.g, fill.b, 0.22f);
            for (var i = 0; i < steps; i++)
            {
                var rect = NewRect("Step", parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x + i * pitch, centreY);
                rect.sizeDelta = new Vector2(blockW, blockH);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = null;
                image.color = i < filled ? fill : empty;
                image.raycastTarget = false;
            }
        }

        /// <summary>The width a StepBar of this many steps takes - so a caller can put
        /// something after it without guessing.</summary>
        public static float StepBarWidth(int steps, float blockW = 5f, float pitch = 7f) =>
            steps <= 0 ? 0f : (steps - 1) * pitch + blockW;

        /// <summary>A pen-drawn bar: a framed trough with an ink fill. What is left for
        /// a genuinely continuous quantity - how far a man is toward his next half
        /// step - where a count of blocks would be a lie about the precision.</summary>
        public static void Bar(Transform parent, float x, float y, float w, float h,
            float fraction, Color fill)
        {
            var rect = NewRect("Bar", parent);
            PlaceTopLeft(rect, x, y, w, h);
            Frame(rect, 1f, LedgerStyle.InkFaint);
            var inner = NewRect("Fill", rect);
            inner.anchorMin = new Vector2(0f, 0f);
            inner.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            inner.offsetMin = new Vector2(2f, 2f);
            inner.offsetMax = new Vector2(-2f * (fraction >= 1f ? 1f : 0f), -2f);
            Fill(inner, fill);
        }

        // ------------------------------------------------------------ ruled paper

        /// <summary>Blue rules every pitch units from y downwards over h, and the red
        /// margin line at marginX - the ledger page under the list.</summary>
        public static void RuledPaper(Transform parent, float x, float y, float w, float h,
            float pitch, float marginX)
        {
            var root = NewRect("Rules", parent);
            PlaceTopLeft(root, x, y, w, h);
            root.gameObject.AddComponent<RectMask2D>();
            for (var line = pitch; line <= h + 0.5f; line += pitch)
                Rule(root, 0f, -line, w, LedgerStyle.RuleBlue);
            if (marginX >= 0f)
                VRule(root, marginX, 0f, h, LedgerStyle.MarginRed);
        }

        /// <summary>A yellow sticky note - the hover notes' paper.
        /// A self-contained root (shadow and paper are its own children) so a note
        /// that moves and resizes after it is built carries its shadow with it.</summary>
        public static RectTransform StickyNote(Transform parent, float w, float h)
        {
            var rect = NewRect("Sticky", parent);
            PlaceTopLeft(rect, 0f, 0f, w, h);
            rect.localRotation = Quaternion.Euler(0f, 0f, 1.5f);

            var shadow = NewRect("Shadow", rect);
            shadow.anchorMin = Vector2.zero;
            shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-8f + 4f, -8f - 5f);
            shadow.offsetMax = new Vector2(8f + 4f, 8f - 5f);
            var image = shadow.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.SoftShadow;
            image.type = Image.Type.Sliced;
            image.color = LedgerStyle.Shadow;
            image.raycastTarget = false;

            var paper = NewRect("Paper", rect);
            Stretch(paper);
            Fill(paper, LedgerStyle.StickyNote);
            return rect;
        }
    }
}
