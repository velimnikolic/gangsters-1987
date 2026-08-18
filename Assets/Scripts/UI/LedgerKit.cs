using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's stationery drawer: every object the book lays on its pages, built
    /// from primitives at runtime. Typed lines, ruled paper, highlighter, label-maker
    /// tape for the verbs, rubber stamps, Polaroids, index cards with a shadow, sticky
    /// notes, gold stars. Coordinates are page coordinates everywhere - x right, y
    /// DOWN as a negative anchoredPosition from the parent's top-left - the one
    /// convention every page shares.
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

        /// <summary>A double rule - the typed line under a heading, the total's underline.</summary>
        public static void DoubleRule(Transform parent, float x, float y, float w, Color color)
        {
            Rule(parent, x, y, w, color);
            Rule(parent, x, y - 3f, w, color);
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

        /// <summary>The soft shadow under a sheet - a 9-sliced blur laid 4 units down
        /// and right, drawn BEFORE the sheet so it sits under it.</summary>
        public static void ShadowUnder(RectTransform sheet, float spread = 10f)
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
            image.color = LedgerStyle.Shadow;
            image.raycastTarget = false;
        }

        /// <summary>A card of stock laid on the page: shadow, fill, grain - all its own
        /// children, so a card that hides, moves or is destroyed takes its shadow with
        /// it. Content goes on the returned rect in its own page coordinates; the
        /// paper's Image is the second child, "Paper", for a caller that wants the
        /// card to catch clicks.</summary>
        public static RectTransform Card(string name, Transform parent, float x, float y,
            float w, float h, Color stock, float tiltDegrees = 0f, float shadowSpread = 10f)
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

        /// <summary>A typed heading: typewriter caps, letter-spaced, with the ruled
        /// line a typist strikes under it. Returns the y below the rule.</summary>
        public static float Heading(Transform parent, float x, float y, float w, string label,
            float size = 15f, bool doubleRule = false)
        {
            var text = Line(parent, LedgerStyle.Type, size, LedgerStyle.Ink, x, y, w,
                size + 10f, label.ToUpperInvariant());
            text.characterSpacing = 3f;
            var ruleY = y - size - 12f;
            if (doubleRule)
                DoubleRule(parent, x, ruleY, w, LedgerStyle.Ink);
            else
                Rule(parent, x, ruleY, w, LedgerStyle.Ink);
            return ruleY - 10f;
        }

        // ------------------------------------------------------- highlighter & marks

        /// <summary>A highlighter stroke across a row - a translucent band a touch
        /// shorter than the row so the pen shows past the ends.</summary>
        public static Image Highlight(RectTransform row, Color color, float inset = 4f)
        {
            var rect = NewRect("Highlight", row);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(inset, 3f);
            rect.offsetMax = new Vector2(-inset, -3f);
            rect.SetAsFirstSibling();
            return Fill(rect, color);
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

        // -------------------------------------------------------------- label tape

        /// <summary>
        /// Label-maker tape: a black (or red) strip with embossed white caps - the
        /// 1980s office's one way of putting a word on a thing. Every verb in the book
        /// is one. The strip is the Button's target graphic; hover lifts, press sinks.
        /// Returns the label so a caller can restyle it (alignment, size, colour).
        /// </summary>
        public static TextMeshProUGUI Tape(Transform parent, string label, float x, float y,
            float w, float h, UnityAction onClick, bool red = false, float size = 12f)
        {
            var rect = NewRect("Tape " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);

            var strip = rect.gameObject.AddComponent<Image>();
            strip.sprite = LedgerStyle.RoundedSmall;
            strip.type = Image.Type.Sliced;
            strip.color = red ? LedgerStyle.TapeRed : LedgerStyle.TapeBlack;
            strip.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = strip;
            var colours = button.colors;
            colours.normalColor = LedgerStyle.TapeNormal;
            colours.highlightedColor = LedgerStyle.TapeHover;
            colours.selectedColor = LedgerStyle.TapeHover;
            colours.pressedColor = LedgerStyle.TapePressed;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text("Label", rect, LedgerStyle.Condensed, size, LedgerStyle.TapeText,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 6f;
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
            float w, float h, float tilt = -8f, float size = 20f)
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
            text.characterSpacing = 6f;
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

            var text = Text("Initials", photo, LedgerStyle.Type, photoSize * 0.34f,
                new Color(0.75f, 0.70f, 0.60f), TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.text = initials;

            var print = NewRect("Print", photo);
            Stretch(print);
            var raw = print.gameObject.AddComponent<RawImage>();
            raw.color = LedgerStyle.PhotoTint;
            raw.raycastTarget = false;
            raw.enabled = false;

            if (caption.Length > 0)
            {
                var cap = Text("Caption", frame, LedgerStyle.Type, 10f, LedgerStyle.InkDim,
                    TextAlignmentOptions.Center);
                PlaceTopLeft(cap.rectTransform, 0f, -(border + photoSize + 2f), w, lip - 4f);
                cap.text = caption;
            }

            return raw;
        }

        // ------------------------------------------------------------------ stars

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

        /// <summary>A pen-drawn bar: a framed trough with an ink fill.</summary>
        public static void Bar(Transform parent, float x, float y, float w, float h,
            float fraction, Color fill)
        {
            var rect = NewRect("Bar", parent);
            PlaceTopLeft(rect, x, y, w, h);
            Frame(rect, 1f, LedgerStyle.Ink);
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
