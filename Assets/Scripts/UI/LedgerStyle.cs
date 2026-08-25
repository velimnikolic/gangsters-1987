using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's look, 1987: a real book on a real desk. Cream stock under a desk
    /// lamp, typewriter and Courier for the words, blue ledger rules with a red margin,
    /// yellow highlighter for the selection, Polaroids for the faces, red rubber stamps
    /// for anything the law has an opinion about, and label-maker tape for the verbs.
    ///
    /// Everything here is generated or loaded at runtime - fonts from Resources as
    /// dynamic TMP assets, sprites and textures drawn once into memory - so the book
    /// dresses itself in any scene with no editor bake and no scene reference. Every
    /// piece degrades: a font that fails to load answers null and TMP keeps its
    /// default face, so the book still reads.
    /// </summary>
    public static class LedgerStyle
    {
        // ------------------------------------------------------------------ colours

        /// <summary>The desk under everything - dark walnut, seen only at the edges.</summary>
        public static readonly Color Desk = new Color(0.16f, 0.11f, 0.08f);

        /// <summary>The desk lamp's pool of light, laid over the desk in the corner.</summary>
        public static readonly Color Lamp = new Color(1f, 0.82f, 0.55f, 0.22f);

        /// <summary>The ledger's cream stock.</summary>
        public static readonly Color Paper = new Color(0.93f, 0.89f, 0.77f);

        /// <summary>The manila folder the pages sit in - the tab strip's colour.</summary>
        public static readonly Color Manila = new Color(0.82f, 0.72f, 0.52f);

        /// <summary>A manila tab not currently pulled forward.</summary>
        public static readonly Color ManilaDim = new Color(0.66f, 0.57f, 0.40f);

        /// <summary>An index card laid on the page - one shade whiter than the stock.</summary>
        public static readonly Color Card = new Color(0.97f, 0.95f, 0.88f);

        /// <summary>The morning paper's newsprint - greyer and colder than the ledger.</summary>
        public static readonly Color Newsprint = new Color(0.89f, 0.87f, 0.80f);

        /// <summary>Accountant's ledger stock - the pale green of a balance sheet.</summary>
        public static readonly Color LedgerGreen = new Color(0.86f, 0.90f, 0.79f);

        /// <summary>A yellow sticky note - the hover notes.</summary>
        public static readonly Color StickyNote = new Color(1f, 0.93f, 0.50f);

        /// <summary>Typewriter ribbon - every word that IS the record.</summary>
        public static readonly Color Ink = new Color(0.09f, 0.08f, 0.07f);

        /// <summary>A lighter strike - captions, tags, the second line.</summary>
        public static readonly Color InkDim = new Color(0.36f, 0.32f, 0.29f);

        /// <summary>Barely there - empty rating boxes, ghosted rows.</summary>
        public static readonly Color InkFaint = new Color(0.14f, 0.12f, 0.11f, 0.32f);

        /// <summary>The red pen: corrections, refusals, anything the boss should worry
        /// about. Handwriting's colour, so it never looks typed.</summary>
        public static readonly Color RedPen = new Color(0.68f, 0.11f, 0.09f);

        /// <summary>The rubber stamp's ink - red, a little uneven, never quite opaque.</summary>
        public static readonly Color StampRed = new Color(0.72f, 0.10f, 0.08f, 0.80f);

        /// <summary>The blue horizontal rules of ledger paper.</summary>
        public static readonly Color RuleBlue = new Color(0.40f, 0.55f, 0.80f, 0.42f);

        /// <summary>The green rules of a balance sheet.</summary>
        public static readonly Color RuleGreen = new Color(0.30f, 0.52f, 0.32f, 0.45f);

        /// <summary>The red margin line down the left of a ruled page.</summary>
        public static readonly Color MarginRed = new Color(0.85f, 0.32f, 0.30f, 0.55f);

        /// <summary>A yellow highlighter pass - the selected row.</summary>
        public static readonly Color Highlighter = new Color(1f, 0.87f, 0.15f, 0.48f);

        /// <summary>A green highlighter pass - a valid drop target in assign mode.</summary>
        public static readonly Color HighlighterGreen = new Color(0.45f, 0.90f, 0.30f, 0.45f);

        /// <summary>Label-maker tape, black - the ordinary verb.</summary>
        public static readonly Color TapeBlack = new Color(0.11f, 0.11f, 0.11f);

        /// <summary>Label-maker tape, red - the verb that commits something.</summary>
        public static readonly Color TapeRed = new Color(0.62f, 0.10f, 0.08f);

        /// <summary>The embossed letters on the tape.</summary>
        public static readonly Color TapeText = new Color(0.97f, 0.96f, 0.90f);

        /// <summary>A tab nobody is reading: card stock a shade under the paper, so it
        /// still takes INK caps. A tape faded toward the page instead would leave its
        /// white letters on near-paper - the word disappears exactly when it is the
        /// word you need to find.</summary>
        public static readonly Color TapeIdle = new Color(0.76f, 0.72f, 0.61f);

        /// <summary>A Polaroid's white border.</summary>
        public static readonly Color PolaroidWhite = new Color(0.98f, 0.97f, 0.93f);

        /// <summary>The unexposed dark inside a Polaroid until the print lands.</summary>
        public static readonly Color PolaroidDark = new Color(0.20f, 0.18f, 0.16f);

        /// <summary>The warm cast a 1987 colour print has after a decade in a drawer.</summary>
        public static readonly Color PhotoTint = new Color(1f, 0.94f, 0.82f);

        /// <summary>The shadow under a card, a note, a Polaroid.</summary>
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.38f);

        /// <summary>Button tint states for a tape: the tint multiplies the tape, so
        /// hover lifts and press sinks. Multipliers, not colours.</summary>
        public static readonly Color TapeNormal = new Color(0.90f, 0.90f, 0.90f);
        public static readonly Color TapeHover = Color.white;
        public static readonly Color TapePressed = new Color(0.62f, 0.62f, 0.62f);

        // -------------------------------------------------------------------- fonts

        static TMP_FontAsset type, mono, monoBold, monoItalic;
        static TMP_FontAsset serif, serifBold, serifItalic, condensed, condensedText;
        static readonly System.Collections.Generic.HashSet<string> missing =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>The typewriter - headings, names, typed labels. Lekton is drawn off
        /// the Olivetti office machines, and unlike a one-cut distressed face it ships a
        /// real bold, so a heading is set in the weight instead of smeared into it.</summary>
        public static TMP_FontAsset Type => Font(ref type, "Lekton-Bold", 0f, 0.53f);

        /// <summary>The ledger's figures and body copy. Fixed pitch is the point - a
        /// column of money only lines up if every digit takes the same width.</summary>
        public static TMP_FontAsset Mono => Font(ref mono, "IBMPlexMono-Regular", 0.05f, 0.85f);
        public static TMP_FontAsset MonoBold => Font(ref monoBold, "IBMPlexMono-Bold", 0f, 0.85f);
        public static TMP_FontAsset MonoItalic => Font(ref monoItalic, "IBMPlexMono-Italic", 0.05f, 0.85f);

        /// <summary>The newspaper's face. PT Serif is cut for newsprint and for a screen,
        /// so it holds its stems at the sizes the book actually prints at.</summary>
        public static TMP_FontAsset Serif => Font(ref serif, "PTSerif-Regular", 0f);
        public static TMP_FontAsset SerifBold => Font(ref serifBold, "PTSerif-Bold", 0f);
        public static TMP_FontAsset SerifItalic => Font(ref serifItalic, "PTSerif-Italic", 0f);

        /// <summary>Tabloid headlines, rubber stamps and label tape. Oswald is the
        /// Alternate Gothic the period's headline decks were actually set in.</summary>
        public static TMP_FontAsset Condensed => Font(ref condensed, "Oswald-Bold", 0f, 0.86f);

        /// <summary>The same gothic at reading weight - the running text of the screens
        /// the city itself wears: map marks, block tags, a popup's line. A deck and its
        /// copy set in one family is what a printed page of the period looked like.
        /// </summary>
        public static TMP_FontAsset CondensedText =>
            Font(ref condensedText, "Oswald-Regular", 0.04f, 0.87f);

        /// <summary>Loads and caches one face. dilate is the SDF face dilation - a face
        /// cut thin needs a hair of it to print like a fresh ribbon at ledger sizes
        /// instead of a worn one. A face that ships the weight asks for none.
        ///
        /// optical is the size the letters come out AT a given point size, which is a
        /// property of the drawing and not of the point size: a typewriter face sits
        /// small inside its em, a headline gothic fills it. Every size in this book was
        /// written against the old faces, so each new one is scaled back to the cap
        /// height it replaces and the numbers on the page keep meaning what they meant.
        /// </summary>
        static TMP_FontAsset Font(ref TMP_FontAsset slot, string name, float dilate,
            float optical = 1f)
        {
            if (slot)
                return slot;
            if (missing.Contains(name))
                return null;

            var source = Resources.Load<Font>("Ledger1987/" + name);
            if (!source)
            {
                // Once per session, not once per text: the book prints hundreds.
                missing.Add(name);
                Debug.LogWarning("[LedgerStyle] Font Ledger1987/" + name +
                                 " not found in Resources - the ledger falls back to " +
                                 "the TMP default face for it.");
                return null;
            }

            slot = TMP_FontAsset.CreateFontAsset(source, 72, 8, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (slot)
            {
                slot.name = "Ledger " + name;
                if (dilate > 0f && slot.material)
                    slot.material.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
                if (!Mathf.Approximately(optical, 1f))
                {
                    // faceInfo is a struct behind a property - read, set, write back, or
                    // the scale lands on a copy and the page is set at the wrong size.
                    var face = slot.faceInfo;
                    face.scale = optical;
                    slot.faceInfo = face;
                }
            }
            else
                missing.Add(name);
            return slot;
        }

        // ------------------------------------------------------------------ sprites

        static Sprite rounded, softShadow, roundedSmall;
        static Texture2D paperGrain, radialLight;

        /// <summary>A 9-sliced rounded rectangle, 6-unit corners - label tape.</summary>
        public static Sprite Rounded => rounded ??= MakeRounded(24, 6f);

        /// <summary>A tighter 3-unit corner - rating boxes, small chips.</summary>
        public static Sprite RoundedSmall => roundedSmall ??= MakeRounded(12, 3f);

        /// <summary>A soft drop shadow, 9-sliced - lay it under a card 4 units off.</summary>
        public static Sprite SoftShadow => softShadow ??= MakeShadow();

        /// <summary>Tileable paper grain: white with a speckled alpha - draw it as a
        /// dark tint over the stock and the page stops being a flat fill.</summary>
        public static Texture2D PaperGrain => paperGrain ??= MakeGrain();

        /// <summary>A radial falloff - the desk lamp's pool.</summary>
        public static Texture2D RadialLight => radialLight ??= MakeRadial();

        // Static state outlives Play when domain reload is off - the runtime-made
        // assets do not, so a stale reference would be a destroyed object.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            type = mono = monoBold = monoItalic = null;
            serif = serifBold = serifItalic = condensed = null;
            missing.Clear();
            rounded = roundedSmall = softShadow = null;
            paperGrain = radialLight = null;
        }

        static Sprite MakeRounded(int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Rounded " + size;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    // Signed distance to the rounded box, 1px anti-aliased edge.
                    var px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    var py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    var outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude
                                  + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    var a = Mathf.Clamp01(0.5f - outside);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var border = Mathf.CeilToInt(radius) + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = tex.name;
            return sprite;
        }

        static Sprite MakeShadow()
        {
            const int size = 48;
            const int fade = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Shadow";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Min(x, size - 1 - x);
                    var dy = Mathf.Min(y, size - 1 - y);
                    var ax = Mathf.Clamp01(dx / (float)fade);
                    var ay = Mathf.Clamp01(dy / (float)fade);
                    // Smoothstep both ways so the corner falls off round, not square.
                    var a = ax * ax * (3f - 2f * ax) * (ay * ay * (3f - 2f * ay));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(fade + 2, fade + 2, fade + 2, fade + 2));
            sprite.name = tex.name;
            return sprite;
        }

        static Texture2D MakeGrain()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Paper Grain";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            // Fixed stream: the same paper every session, and no shared rng disturbed.
            var rng = new System.Random(1987);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    // Fine speckle plus a slow blotch - fibre and foxing. Perlin
                    // tiles because the sample wraps at the texture's own period.
                    var speck = (float)rng.NextDouble();
                    var u = x / (float)size;
                    var v = y / (float)size;
                    var blotch = Mathf.PerlinNoise(u * 6f + 11.3f, v * 6f + 4.7f);
                    var a = speck * 0.11f + blotch * 0.08f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D MakeRadial()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Radial";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                        new Vector2(half, half)) / half;
                    var a = 1f - Mathf.Clamp01(d);
                    a = a * a * (3f - 2f * a);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
