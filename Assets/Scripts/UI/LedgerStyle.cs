using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's look, 1987: a manila file open on a walnut desk, not a UI panel.
    /// Cream stock under a warm ceiling light, typewriter and fixed-pitch for the
    /// words, a red rubber stamp for anything the law has an opinion about, punched
    /// holes down both edges, a blotter strip of readouts, and telex slips clipped in
    /// where the night's word came through.
    ///
    /// The values are the 1987 redesign's tokens, kept as hex the way the handoff
    /// writes them so a colour can be checked against the sheet without arithmetic.
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

        /// <summary>0xRRGGBB the way the design handoff writes it.</summary>
        static Color Rgb(uint hex) => new Color(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f);

        static Color Rgb(uint hex, float alpha)
        {
            var colour = Rgb(hex);
            colour.a = alpha;
            return colour;
        }

        // ---- the desk ----

        /// <summary>Dark walnut, the top of the desk's gradient.</summary>
        public static readonly Color Desk = Rgb(0x2a1f14);

        /// <summary>Where the desk falls away from the light, 55% down.</summary>
        public static readonly Color DeskMid = Rgb(0x16100a);

        /// <summary>The far edge of the desk, out of the lamp entirely.</summary>
        public static readonly Color DeskDeep = Rgb(0x0d0906);

        /// <summary>The warm glow off the ceiling fixture, laid over the desk's top.</summary>
        public static readonly Color Lamp = new Color(226f / 255f, 187f / 255f, 120f / 255f, 0.20f);

        // ---- stock ----

        /// <summary>The ledger's aged cream sheet - the top of its gradient.</summary>
        public static readonly Color Paper = Rgb(0xf0e6cd);
        public static readonly Color PaperMid = Rgb(0xe7dabb);
        public static readonly Color PaperDeep = Rgb(0xddcfae);

        /// <summary>The manila shell the sheet sits in.</summary>
        public static readonly Color Manila = Rgb(0xcdb387);
        public static readonly Color ManilaLow = Rgb(0xc2a67a);

        /// <summary>A divider tab nobody has pulled forward.</summary>
        public static readonly Color ManilaDim = Rgb(0xb59c72);
        public static readonly Color ManilaDimLow = Rgb(0xa68d64);

        /// <summary>The dossier's stock - one shade whiter than the sheet.</summary>
        public static readonly Color Card = Rgb(0xf7f2e2);
        public static readonly Color CardLow = Rgb(0xefe6cf);

        /// <summary>Line-printer paper: the payroll printout and the armory cards.</summary>
        public static readonly Color Printout = Rgb(0xfbf6e6);
        public static readonly Color PrintoutLow = Rgb(0xf3ecd6);

        /// <summary>A rolodex card - the families.</summary>
        public static readonly Color IndexCard = Rgb(0xfdf9ec);
        public static readonly Color IndexCardLow = Rgb(0xf4eedb);

        /// <summary>A telex slip, off the machine and still curling.</summary>
        public static readonly Color Slip = Rgb(0xfdfaf0);
        public static readonly Color SlipLow = Rgb(0xf2ebd8);

        /// <summary>The morning paper's newsprint - greyer and colder than the ledger.</summary>
        public static readonly Color Newsprint = Rgb(0xeee8d8);
        public static readonly Color NewsprintLow = Rgb(0xe4dcc7);

        /// <summary>Accountant's greenbar - the pale green of a balance sheet.</summary>
        public static readonly Color LedgerGreen = Rgb(0xe9eede);
        public static readonly Color LedgerGreenLow = Rgb(0xdfe6d2);

        /// <summary>The band a greenbar sheet prints every other line.</summary>
        public static readonly Color GreenbarBand = new Color(146f / 255f, 176f / 255f,
            140f / 255f, 0.42f);

        /// <summary>The ink a balance sheet is set in - green-black, not black.</summary>
        public static readonly Color GreenbarInk = Rgb(0x22301c);

        /// <summary>A carbon copy: the stock book's pink second sheet.</summary>
        public static readonly Color Carbon = Rgb(0xf6dfd9);
        public static readonly Color CarbonLow = Rgb(0xeed3cb);

        /// <summary>The ink a carbon copy comes out in - dull, bled, never black.</summary>
        public static readonly Color CarbonInk = Rgb(0x6b2b23);

        /// <summary>A yellow sticky note - the hover notes.</summary>
        public static readonly Color StickyNote = new Color(1f, 0.93f, 0.50f);

        // ---- ink ----

        /// <summary>Typewriter ribbon - every word that IS the record.</summary>
        public static readonly Color Ink = Rgb(0x241f1a);

        /// <summary>A softer strike - body copy that is not a figure.</summary>
        public static readonly Color InkSoft = Rgb(0x3b3226);

        /// <summary>Mid ink - a second line, a note under a total.</summary>
        public static readonly Color InkMid = Rgb(0x4a3f2c);

        /// <summary>Muted - captions, sublines, the tag after a name.</summary>
        public static readonly Color InkDim = Rgb(0x6d5c40);

        /// <summary>Faded - a footer ticker, an aside nobody has to read.</summary>
        public static readonly Color InkPale = Rgb(0x7a684a);

        /// <summary>The small-caps label over a value - the quietest type on the page.</summary>
        public static readonly Color InkLabel = Rgb(0x8a7756);

        /// <summary>The hairline a form is ruled with - barely a mark.</summary>
        public static readonly Color InkFaint = new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.22f);

        /// <summary>Fainter still - a row's bottom rule under a column of names.</summary>
        public static readonly Color InkHair = new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.10f);

        /// <summary>The dotted leader between a label and its figure. Stronger than a
        /// hairline on purpose: half of a dotted rule is gaps, so at a hairline's alpha
        /// it averages out to nothing at all.</summary>
        public static readonly Color InkDotted = new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.40f);

        // ---- red ----

        /// <summary>The red pen and the alert figure: corrections, refusals, money
        /// running the wrong way. Handwriting's colour, so it never looks typed.</summary>
        public static readonly Color RedPen = Rgb(0x8f2119);

        /// <summary>The rubber stamp's ink - never quite opaque, never quite square.</summary>
        public static readonly Color StampRed = Rgb(0x96281f, 0.85f);

        /// <summary>Bled red - a carbon's heading, a pink sheet's rule.</summary>
        public static readonly Color DeepRed = Rgb(0x6b2b23);

        /// <summary>Soft red - a countdown that has not run out yet.</summary>
        public static readonly Color SoftRed = Rgb(0xe79a8c);

        /// <summary>The green a form marks ACTIVE in.</summary>
        public static readonly Color GreenOk = Rgb(0x3f6b3a);

        /// <summary>Ballpoint blue - anything written by hand in the margin.</summary>
        public static readonly Color Ballpoint = Rgb(0x2f4a7a);

        // ---- the blotter strip ----

        /// <summary>The desk blotter under the sheet's readouts - the top of it.</summary>
        public static readonly Color Blotter = Rgb(0x221a11);
        public static readonly Color BlotterLow = Rgb(0x171009);

        /// <summary>The hairline between two blotter cells.</summary>
        public static readonly Color BlotterRule = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>A blotter figure worth a second look.</summary>
        public static readonly Color HudAmber = Rgb(0xe0b464);

        /// <summary>An ordinary blotter figure.</summary>
        public static readonly Color HudCream = Rgb(0xf0e3c2);

        /// <summary>The small-caps label over a blotter figure.</summary>
        public static readonly Color HudLabel = Rgb(0x9a8560);

        /// <summary>The sub-note under a blotter meter.</summary>
        public static readonly Color HudNote = Rgb(0x7c6a4a);

        /// <summary>A meter running warm - the heat bar.</summary>
        public static readonly Color HudMeterWarm = Rgb(0xc97a4a);

        // ---- rules and marks ----

        /// <summary>The blue horizontal rules of ledger paper.</summary>
        public static readonly Color RuleBlue = new Color(47f / 255f, 74f / 255f, 122f / 255f, 0.16f);

        /// <summary>The green rules of a balance sheet.</summary>
        public static readonly Color RuleGreen = new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.22f);

        /// <summary>The red margin line down the left of a ruled page.</summary>
        public static readonly Color MarginRed = new Color(143f / 255f, 33f / 255f, 25f / 255f, 0.35f);

        /// <summary>A yellow highlighter pass - the selected row.</summary>
        public static readonly Color Highlighter = new Color(143f / 255f, 33f / 255f, 25f / 255f, 0.09f);

        /// <summary>A green highlighter pass - a valid drop target in assign mode.</summary>
        public static readonly Color HighlighterGreen = new Color(63f / 255f, 107f / 255f,
            58f / 255f, 0.14f);

        /// <summary>The punched holes down both edges of the sheet.</summary>
        public static readonly Color Punch = new Color(0.05f, 0.035f, 0.02f, 0.55f);

        /// <summary>The ring a coffee cup left on the top right of the file.</summary>
        public static readonly Color CoffeeRing = new Color(120f / 255f, 78f / 255f, 32f / 255f, 0.16f);

        /// <summary>A Polaroid's white border.</summary>
        public static readonly Color PolaroidWhite = new Color(0.98f, 0.97f, 0.93f);

        /// <summary>The unexposed dark inside a Polaroid until the print lands.</summary>
        public static readonly Color PolaroidDark = Rgb(0xe3d8bd);

        /// <summary>The warm cast a 1987 colour print has after a decade in a drawer.</summary>
        public static readonly Color PhotoTint = new Color(1f, 0.94f, 0.82f);

        /// <summary>The steel of the paperclip on the dossier's top edge.</summary>
        public static readonly Color Paperclip = new Color(90f / 255f, 90f / 255f, 96f / 255f, 0.75f);

        /// <summary>The shadow under a card, a note, a Polaroid.</summary>
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.38f);

        /// <summary>The shadow under the whole folder - deep, from one light above.</summary>
        public static readonly Color FolderShadow = new Color(0f, 0f, 0f, 0.55f);

        // ---- buttons ----

        /// <summary>The dark action button: PROMOTE, ORDER, COMMIT.</summary>
        public static readonly Color TapeBlack = Rgb(0x1d1812);

        /// <summary>The red one - the verb that cannot be taken back.</summary>
        public static readonly Color TapeRed = Rgb(0x8f2119);

        /// <summary>The letters on a dark button.</summary>
        public static readonly Color TapeText = Rgb(0xefe4c9);

        /// <summary>A pill nobody has chosen: a wash of ink on the sheet.</summary>
        public static readonly Color TapeIdle = new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.10f);

        /// <summary>Button tint states: the tint multiplies the face, so hover lifts
        /// and press sinks. Multipliers, not colours.</summary>
        public static readonly Color TapeNormal = new Color(0.90f, 0.90f, 0.90f);
        public static readonly Color TapeHover = Color.white;
        public static readonly Color TapePressed = new Color(0.62f, 0.62f, 0.62f);

        // -------------------------------------------------------------------- fonts

        static TMP_FontAsset type, mono, monoBold, monoItalic;
        static TMP_FontAsset serif, serifBold, serifItalic, condensed, condensedText;
        static TMP_FontAsset pixel, pixelBold;
        static readonly System.Collections.Generic.HashSet<string> missing =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>The typewriter - headings, names, typed labels. Lekton is drawn off
        /// the Olivetti office machines, and unlike a one-cut distressed face it ships a
        /// real bold, so a heading is set in the weight instead of smeared into it.
        ///
        /// Its optical figure is the one measured, not the one assumed: Special Elite's
        /// caps do not fill 0.35 of its em - the H glyph measures 51.0 units on a 72
        /// em, 0.709 - against Lekton's 47.2, 0.655. The ratio is 1.082, and the 0.53
        /// this carried before printed every typed word in the book at half height.</summary>
        public static TMP_FontAsset Type => Font(ref type, "Lekton-Bold", 0f, 1.082f);

        /// <summary>The ledger's figures and body copy. Fixed pitch is the point - a
        /// column of money only lines up if every digit takes the same width.</summary>
        public static TMP_FontAsset Mono => Font(ref mono, "IBMPlexMono-Regular", 0.05f, 0.831f);
        public static TMP_FontAsset MonoBold => Font(ref monoBold, "IBMPlexMono-Bold", 0f, 0.831f);
        public static TMP_FontAsset MonoItalic => Font(ref monoItalic, "IBMPlexMono-Italic", 0.05f, 0.831f);

        /// <summary>The newspaper's face, and the hand in the margin. PT Serif is cut
        /// for newsprint and for a screen, so it holds its stems at the sizes the book
        /// actually prints at.</summary>
        public static TMP_FontAsset Serif => Font(ref serif, "PTSerif-Regular", 0f, 1.017f);
        public static TMP_FontAsset SerifBold => Font(ref serifBold, "PTSerif-Bold", 0f, 1.017f);
        public static TMP_FontAsset SerifItalic => Font(ref serifItalic, "PTSerif-Italic", 0f, 1.017f);

        /// <summary>Stamped chrome: the masthead, the headline decks, the rubber stamps,
        /// every small-caps label. Oswald is the Alternate Gothic the period's headline
        /// decks were actually set in.</summary>
        public static TMP_FontAsset Condensed => Font(ref condensed, "Oswald-Bold", 0f, 0.864f);

        /// <summary>The same gothic at reading weight - the running text of the screens
        /// the city itself wears: map marks, block tags, a popup's line. A deck and its
        /// copy set in one family is what a printed page of the period looked like.
        /// </summary>
        public static TMP_FontAsset CondensedText =>
            Font(ref condensedText, "Oswald-Regular", 0.04f, 0.864f);

        /// <summary>
        /// The one face that is not type but PIXELS: Silkscreen, the bitmap gothic an
        /// early-VGA survey terminal printed in. Kept for a screen that wants pixels;
        /// the turf map is set in the gothic above. It is built by <see cref="Bitmap"/>
        /// rather than <see cref="Font"/>, because everything the other five faces want
        /// - an SDF atlas, antialiasing, a hair of face dilation - is exactly what turns
        /// a pixel font into a smear. Rendered as a RASTER atlas with no padding and
        /// sampled with a point filter, so a letter drawn at 16 px is the 8 px letter
        /// with square pixels twice the size, which is what the design asks for.
        ///
        /// No optical figure: Silkscreen's sizes were not inherited from an older face,
        /// they were authored against this one.
        /// </summary>
        public static TMP_FontAsset Pixel => Bitmap(ref pixel, "Silkscreen-Regular");

        /// <summary>The same terminal's headings and figures.</summary>
        public static TMP_FontAsset PixelBold => Bitmap(ref pixelBold, "Silkscreen-Bold");

        /// <summary>Loads and caches one face. dilate is the SDF face dilation - a face
        /// cut thin needs a hair of it to print like a fresh ribbon at ledger sizes
        /// instead of a worn one. A face that ships the weight asks for none.
        ///
        /// optical is the size the letters come out AT a given point size, which is a
        /// property of the drawing and not of the point size: a typewriter face sits
        /// small inside its em, a headline gothic fills it. Every size in this book was
        /// written against the old faces, so each new one is scaled back to the cap
        /// height it replaces and the numbers on the page keep meaning what they meant.
        ///
        /// The figure is a measurement, taken off the H glyph of both faces at the same
        /// em - cap height as a fraction of the em, old over new:
        ///
        ///   Special Elite 0.709 / Lekton      0.655 = 1.082   Type
        ///   Courier Prime 0.580 / IBM Plex    0.698 = 0.831   Mono
        ///   Old Standard  0.712 / PT Serif    0.700 = 1.017   Serif
        ///   Barlow Cond.  0.700 / Oswald      0.810 = 0.864   Condensed
        ///
        /// It scales the set width with the height, so whether a column still fits is
        /// a second question, answered by how wide the new face is FOR its cap height -
        /// H's advance over H's ink height, old against new:
        ///
        ///   Special Elite 0.921 -> Lekton     0.763   narrower, room to spare
        ///   Courier Prime 1.035 -> IBM Plex   0.860   narrower
        ///   Old Standard  1.106 -> PT Serif   1.039   narrower
        ///   Barlow Cond.  0.686 -> Oswald     0.753   WIDER by a tenth
        ///
        /// Oswald is the one face that can burst a rect Barlow fitted. Per letter, not
        /// per line: set an actual tape and the letterspacing swallows the difference -
        /// "SORT: ROSTER ORDER" measures 121.0 in Barlow and 122.5 in Oswald. So the
        /// tapes clear their rects, and only a long unspaced Condensed line is worth
        /// measuring before it is placed. The other three faces cannot overrun at all.
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
                InkBleed(slot.material);
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

        /// <summary>
        /// The pixel path. A bitmap face has ONE right size - the size it was drawn at -
        /// and every mechanism <see cref="Font"/> uses to make an outline print well at
        /// any size destroys it: an SDF atlas rounds the corners off a 3x5 letter, and a
        /// bilinear sample turns each of its pixels into a grey smudge. So the atlas is
        /// rasterised at the face's own 8 px with no padding and read back with a point
        /// filter, and the HUD only ever asks for whole multiples of that size.
        ///
        /// SampleSize is the drawn size and not a taste: Silkscreen's grid is 8 px tall,
        /// and rasterising it at anything else lands its stems between texels - which is
        /// how a pixel font ends up with one column of a letter a shade lighter than the
        /// rest of it.
        /// </summary>
        static TMP_FontAsset Bitmap(ref TMP_FontAsset slot, string name)
        {
            const int SampleSize = 8;

            if (slot)
                return slot;
            if (missing.Contains(name))
                return null;

            var source = Resources.Load<Font>("Ledger1987/" + name);
            if (!source)
            {
                missing.Add(name);
                Debug.LogWarning("[LedgerStyle] Pixel font Ledger1987/" + name +
                                 " not found in Resources - the tactical map falls back " +
                                 "to the TMP default face.");
                return null;
            }

            // One pixel of padding, not none. A raster atlas is packed tight without
            // it and neighbouring glyphs bleed into each other's cells - which prints
            // as letters wearing pieces of the letter next to them.
            slot = TMP_FontAsset.CreateFontAsset(source, SampleSize, 1,
                GlyphRenderMode.RASTER, 512, 512, AtlasPopulationMode.Dynamic, true);
            if (!slot)
            {
                missing.Add(name);
                return null;
            }

            slot.name = "Ledger " + name;

            // The whole point of the face: nearest-neighbour on the way out, so a
            // glyph scaled to 16 or 24 px is still made of square pixels.
            if (slot.atlasTexture)
                slot.atlasTexture.filterMode = FilterMode.Point;
            var atlases = slot.atlasTextures;
            if (atlases != null)
                for (var i = 0; i < atlases.Length; i++)
                    if (atlases[i])
                        atlases[i].filterMode = FilterMode.Point;

            return slot;
        }

        /// <summary>
        /// The ink bleed: every typed letter in the book sits in a faint halo of its own
        /// ink, the way type pressed into soft paper wicks a fraction into the fibre.
        ///
        /// Done on the FACE's shared material - a runtime TMP font asset owns exactly one
        /// - so the whole book gets it for one material and no extra draw calls. The
        /// per-label alternative (outlineWidth on a TMP_Text) instantiates a material per
        /// label, and this page prints hundreds of them.
        ///
        /// A zero-offset underlay rather than a drop shadow: the design asks for
        /// `0 0 .4px`, which is a bleed in every direction and not a light source.
        /// </summary>
        static void InkBleed(Material material)
        {
            if (!material || !material.HasProperty(ShaderUtilities.ID_UnderlayColor))
                return;

            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor,
                new Color(36f / 255f, 31f / 255f, 26f / 255f, 0.55f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.05f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.09f);
        }

        // ------------------------------------------------------------------ sprites

        static Sprite rounded, softShadow, roundedSmall, disc, ring;
        static Texture2D paperGrain, radialLight, hatch, deskFall, sheetFall, deskStripe;
        static Texture2D dotRule, fadeUp;
        static Texture2D halftone, foxing, crease, vignette, speckle;

        /// <summary>A 9-sliced rounded rectangle, 6-unit corners - the folder's shell.</summary>
        public static Sprite Rounded => rounded ??= MakeRounded(24, 6f);

        /// <summary>A tighter 3-unit corner - the divider tabs. The design's radius
        /// scale stops here: everything else on the sheet is square.</summary>
        public static Sprite RoundedSmall => roundedSmall ??= MakeRounded(12, 3f);

        /// <summary>A soft drop shadow, 9-sliced - lay it under a card 4 units off.</summary>
        public static Sprite SoftShadow => softShadow ??= MakeShadow();

        /// <summary>A hard-edged filled circle - a punched hole, a torn perforation.</summary>
        public static Sprite Disc => disc ??= MakeDisc();

        /// <summary>A circle drawn as an outline - the coffee ring.</summary>
        public static Sprite Ring => ring ??= MakeRing();

        /// <summary>Tileable paper grain: white with a speckled alpha - draw it as a
        /// dark tint over the stock and the page stops being a flat fill.</summary>
        public static Texture2D PaperGrain => paperGrain ??= MakeGrain();

        /// <summary>A radial falloff - the ceiling light's pool over the desk.</summary>
        public static Texture2D RadialLight => radialLight ??= MakeRadial();

        /// <summary>45 degree hatch, 3 on 7 - what stands in for art the game has not
        /// photographed yet, and the fill inside a catalogue plate.</summary>
        public static Texture2D Hatch => hatch ??= MakeHatch();

        /// <summary>The desk's own vertical fall: walnut at the top, all but black at
        /// the bottom edge. One column of pixels, stretched.</summary>
        public static Texture2D DeskFall => deskFall ??= MakeFall(Desk, DeskMid, DeskDeep, 0.55f);

        /// <summary>The sheet's fall - cream at the head, foxed toward the foot.</summary>
        public static Texture2D SheetFall => sheetFall ??= MakeFall(Paper, PaperMid, PaperDeep, 0.60f);

        /// <summary>The desk's grain, near-vertical, at the edge of visibility.</summary>
        public static Texture2D DeskStripe => deskStripe ??= MakeStripe();

        /// <summary>A dotted rule, four units to the dot - the leader between a label
        /// and the figure it belongs to. Tiled along a 1-unit-tall rect.</summary>
        public static Texture2D DotRule => dotRule ??= MakeDotRule();

        /// <summary>Transparent at the head, opaque at the foot. Tint it with a stock's
        /// darker stop and lay it over the flat fill: that is a two-stop gradient for
        /// any pair of colours, at the cost of one shared 64-texel column.</summary>
        public static Texture2D FadeUp => fadeUp ??= MakeFadeUp();

        /// <summary>A real halftone: two dot grids at 4 and 6 units, offset from each
        /// other, the coarse one darker. What a 1987 photograph in a typed file actually
        /// looks like - and what a 45-degree line hatch never did.</summary>
        public static Texture2D Halftone => halftone ??= MakeHalftone();

        /// <summary>A foxing blotch - the rust-brown bloom old paper grows where it was
        /// damp. Soft-edged and irregular; three of them go on a sheet.</summary>
        public static Texture2D Foxing => foxing ??= MakeFoxing();

        /// <summary>The fold: dark on the upper side of the crease line, bright on the
        /// lower, over a fourteen-unit band. A sheet that was folded once and flattened
        /// out again catches the light differently either side of the line.</summary>
        public static Texture2D Crease => crease ??= MakeCrease();

        /// <summary>The lamp's opposite: clear in the middle, closing to darkness at the
        /// edges. Laid over the WHOLE screen, above the folder, so the file sits in a
        /// pool of light instead of on a lit rectangle.</summary>
        public static Texture2D Vignette => vignette ??= MakeVignette();

        /// <summary>The desk's fine speckle - dust and the grain of an old finish, at
        /// three units. Under the stripe, over the fall.</summary>
        public static Texture2D Speckle => speckle ??= MakeSpeckle();

        // Static state outlives Play when domain reload is off - the runtime-made
        // assets do not, so a stale reference would be a destroyed object.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            type = mono = monoBold = monoItalic = null;
            serif = serifBold = serifItalic = condensed = condensedText = null;
            // the pixel faces are made the same way and die the same death - a runtime
            // TMP_FontAsset does not survive leaving Play, and with domain reload off
            // the static that held it does
            pixel = pixelBold = null;
            missing.Clear();
            rounded = roundedSmall = softShadow = disc = ring = null;
            paperGrain = radialLight = hatch = deskFall = sheetFall = deskStripe = null;
            dotRule = fadeUp = null;
            halftone = foxing = crease = vignette = speckle = null;
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

        static Sprite MakeDisc()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Disc";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                        new Vector2(half, half));
                    // One pixel of anti-aliasing at the rim, none anywhere else.
                    var a = Mathf.Clamp01(half - 0.5f - d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            // No 9-slice: a disc scaled by its borders stops being a disc.
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            return sprite;
        }

        static Sprite MakeRing()
        {
            const int size = 128;
            // The cup's wall, as a fraction of the radius - a 9-unit border on a
            // 132-unit ring is what the design draws.
            const float wall = 9f / 66f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Ring";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            var outer = half - 1f;
            var inner = outer * (1f - wall);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                        new Vector2(half, half));
                    var a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
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
                    // Fine speckle plus a slow blotch - fibre and foxing - over the
                    // 4-unit horizontal ruling the design's stock is laid on.
                    var speck = (float)rng.NextDouble();
                    var u = x / (float)size;
                    var v = y / (float)size;
                    var blotch = Mathf.PerlinNoise(u * 6f + 11.3f, v * 6f + 4.7f);
                    var ruling = y % 4 == 0 ? 0.05f : 0f;
                    var a = speck * 0.09f + blotch * 0.07f + ruling;
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

        /// <summary>The 45 degree 3-on-7 hatch a printer's block-out is filled with.
        /// The period is 10 units on the diagonal, which tiles exactly on a 20-unit
        /// square - so the texture repeats with no seam at any size.</summary>
        static Texture2D MakeHatch()
        {
            const int size = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Hatch";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var phase = (x + y) % 10;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(phase < 3 ? 46 : 0));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>A three-stop vertical gradient, one pixel wide. Stretched over a
        /// rect it IS the CSS gradient the design specifies, at a cost of 64 texels.
        /// Row 0 is the BOTTOM in texture space, so the stops are laid in reverse.</summary>
        static Texture2D MakeFall(Color top, Color mid, Color foot, float midStop)
        {
            const int size = 64;
            var tex = new Texture2D(1, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Fall";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size];
            for (var y = 0; y < size; y++)
            {
                var down = 1f - (y + 0.5f) / size;
                var colour = down <= midStop
                    ? Color.Lerp(top, mid, down / midStop)
                    : Color.Lerp(mid, foot, (down - midStop) / (1f - midStop));
                pixels[y] = colour;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D MakeFadeUp()
        {
            const int size = 64;
            var tex = new Texture2D(1, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Fade";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size];
            for (var y = 0; y < size; y++)
            {
                // Row 0 is the foot of the rect in texture space.
                var down = 1f - (y + 0.5f) / size;
                pixels[y] = new Color32(255, 255, 255, (byte)(down * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D MakeDotRule()
        {
            const int size = 4;
            var tex = new Texture2D(size, 1, TextureFormat.RGBA32, false);
            tex.name = "Ledger Dot Rule";
            tex.wrapMode = TextureWrapMode.Repeat;
            // Point, not bilinear: a two-on-two dot smeared by filtering is a grey line.
            tex.filterMode = FilterMode.Point;
            tex.SetPixels32(new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0),
            });
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// A true halftone screen. Two grids of dots - a coarse one on a six-unit pitch
        /// at 85% ink, a fine one on four at 45%, offset half a cell from each other so
        /// they never line up into a plaid. Twelve units is the least common multiple of
        /// the two pitches, so the tile repeats with no seam and no beat frequency.
        ///
        /// Point-filtered: a halftone dot is a HARD dot, and bilinear turns a screen
        /// into grey mud at exactly the size a mug shot is printed.
        /// </summary>
        static Texture2D MakeHalftone()
        {
            const int size = 24;          // 12 logical units at 2 texels each
            const int coarse = 12;        // a 6-unit pitch
            const int fine = 8;           // a 4-unit pitch
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Halftone";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var a = 0f;

                    // The coarse screen, centred in its cell.
                    var cx = x % coarse - coarse * 0.5f + 0.5f;
                    var cy = y % coarse - coarse * 0.5f + 0.5f;
                    if (cx * cx + cy * cy <= 4.4f)
                        a = 0.85f;

                    // The fine screen, offset half a cell so the two never coincide.
                    var fx = (x + fine / 2) % fine - fine * 0.5f + 0.5f;
                    var fy = (y + fine / 2) % fine - fine * 0.5f + 0.5f;
                    if (fx * fx + fy * fy <= 1.4f && a < 0.45f)
                        a = 0.45f;

                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// One foxing blotch: a soft brown bloom, deliberately off-round. The radius is
        /// modulated by the angle so no two directions fall off alike - a perfectly
        /// circular stain reads as a UI element, which is the one thing it must not.
        /// </summary>
        static Texture2D MakeFoxing()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Foxing";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5f - half;
                    var dy = y + 0.5f - half;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) / half;

                    // Two low harmonics wobble the edge; the phases are arbitrary and
                    // fixed, so the same blotch comes out every run.
                    var angle = Mathf.Atan2(dy, dx);
                    var wobble = 1f + 0.16f * Mathf.Sin(angle * 3f + 0.7f)
                                    + 0.09f * Mathf.Sin(angle * 5f - 1.9f);

                    var a = 1f - Mathf.Clamp01(d / (0.86f * wobble));
                    a = a * a;                       // soft, damp-edged, not a disc
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The fold, as a one-column band: shadow above the crease line and a
        /// lifted highlight below it, both dying out over seven units either way. Laid
        /// across a sheet at the height it was folded.</summary>
        static Texture2D MakeCrease()
        {
            const int size = 32;             // the 14-unit band, oversampled
            var tex = new Texture2D(1, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Crease";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size];
            for (var y = 0; y < size; y++)
            {
                // Row 0 is the BOTTOM of the band. Below the line the paper catches the
                // light; above it, it shades.
                var t = (y + 0.5f) / size;               // 0 at foot, 1 at head
                var fromLine = (t - 0.5f) * 2f;          // -1 below, +1 above
                var fall = 1f - Mathf.Abs(fromLine);
                fall *= fall;

                pixels[y] = fromLine >= 0f
                    ? new Color32(60, 48, 30, (byte)(fall * 60f))       // shadow above
                    : new Color32(255, 250, 236, (byte)(fall * 70f));   // highlight below
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The vignette: clear through the middle two-fifths, closing to a
        /// heavy dark at the corners. The inverse curve of the lamp, and drawn over
        /// everything rather than under it - the file is IN the light, not beside it.</summary>
        static Texture2D MakeVignette()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Vignette";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f - half) / half;
                    var dy = (y + 0.5f - half) / half;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Nothing at all inside 0.40, then a smooth close to full at the
                    // corner - the design's own stop.
                    var t = Mathf.Clamp01((d - 0.40f) / 0.75f);
                    t = t * t * (3f - 2f * t);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(t * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The desk's speckle: sparse single-texel dust on a three-unit grid,
        /// dealt from a fixed seed so the desk is the same desk every run.</summary>
        static Texture2D MakeSpeckle()
        {
            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Speckle";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            var rng = new System.Random(1987);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                var roll = rng.Next(0, 100);
                var a = roll < 4 ? 0.22f : roll < 12 ? 0.10f : 0f;
                pixels[i] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The desk's stripe: a near-vertical grain at 2% - the one texture on
        /// the screen that is meant not to be noticed.</summary>
        static Texture2D MakeStripe()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ledger Desk Stripe";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    // 96 degrees off horizontal: one unit of lean over ten of rise.
                    var lean = x + y / 10;
                    var a = lean % 6 < 2 ? 0.05f : 0f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
