using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The paper the turf map is printed on: one 960 x 600 buffer of Color32 and the
    /// half-dozen marks a 1987 survey plate is made of.
    ///
    /// Everything on this map lives in TWO resolutions at once and that split is the
    /// whole look. STRUCTURE - land, water, carriageways, block pads, building
    /// footprints - is authored in a 320 x 200 unit space and multiplied by
    /// <see cref="S"/> at draw time, so the city blocks out chunky. DETAIL - the ink
    /// hairline round a footprint, the kerb ribbon, the stipple on the ground, a lane
    /// dash - is written at one real pixel, never scaled. Two primitives keep the two
    /// apart and nothing else may draw: <see cref="Fill"/> takes authored units,
    /// <see cref="Px"/> takes raster pixels. A single "draw a rect" helper that
    /// guessed which one you meant is exactly how the plate loses its bite.
    ///
    /// No anti-aliasing anywhere. Every write is an integer-aligned block of solid
    /// colour, the texture is point-filtered, and the one blend that exists
    /// (<see cref="Over"/>) mixes whole pixels rather than feathering an edge.
    ///
    /// The buffer is plain managed memory and the class knows nothing about Unity
    /// beyond Color32 and Texture2D, so a plate can be built on a worker thread or in
    /// a test with no scene at all.
    /// </summary>
    public sealed class TurfPlate
    {
        /// <summary>Authored space: the coordinates every piece of city geometry is
        /// converted into before it is drawn. 320 x 200 is the reference plate's
        /// field and the aspect the whole design was composed against.</summary>
        public const int AW = 320, AH = 200;

        /// <summary>Raster pixels per authored unit. Three is what makes a two-unit
        /// house six pixels across - big enough to carry an ink outline and a lighter
        /// core, small enough that a district still fits the plate.</summary>
        public const int S = 3;

        /// <summary>The render target, exactly. Never derived from the screen: the
        /// plate is upscaled to fit, the way a printed sheet is held closer.</summary>
        public const int RW = AW * S, RH = AH * S;

        readonly Color32[] _pixels = new Color32[RW * RH];

        // ------------------------------------------------------------- raster ink

        /// <summary>One block of solid colour in RASTER pixels. Clipped, never
        /// wrapped: a mark that runs off the sheet is cut at the edge.</summary>
        public void Px(int x, int y, int w, int h, Color32 colour)
        {
            if (w <= 0 || h <= 0)
                return;

            int x0 = Mathf.Max(0, x), y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(RW, x + w), y1 = Mathf.Min(RH, y + h);
            for (int row = y0; row < y1; row++)
            {
                int at = row * RW + x0;
                for (int col = x0; col < x1; col++)
                    _pixels[at++] = colour;
            }
        }

        /// <summary>One pixel, unclipped-checked - the inner loop of every edge
        /// detector on the plate, so it is worth its own entry.</summary>
        public void Dot(int x, int y, Color32 colour)
        {
            if (x < 0 || y < 0 || x >= RW || y >= RH)
                return;
            _pixels[y * RW + x] = colour;
        }

        /// <summary>A block laid OVER what is already there, at the colour's own
        /// alpha. Straight source-over on whole pixels - a zebra bar at 80%, a crew's
        /// glow, an order marker fading out. Never an edge feather: the alpha varies
        /// between marks, never inside one, so nothing on the sheet grows a grey
        /// fringe.
        ///
        /// The destination's alpha is composited too, which is what lets the moving
        /// layer be drawn onto a transparent plate and still stack correctly over the
        /// ground beneath it.</summary>
        public void Over(int x, int y, int w, int h, Color32 colour)
        {
            if (w <= 0 || h <= 0 || colour.a == 0)
                return;

            if (colour.a == 255)
            {
                Px(x, y, w, h, colour);
                return;
            }

            int a = colour.a;
            int x0 = Mathf.Max(0, x), y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(RW, x + w), y1 = Mathf.Min(RH, y + h);
            for (int row = y0; row < y1; row++)
            {
                int at = row * RW + x0;
                for (int col = x0; col < x1; col++)
                {
                    var had = _pixels[at];
                    _pixels[at++] = new Color32(
                        (byte)((had.r * (255 - a) + colour.r * a) / 255),
                        (byte)((had.g * (255 - a) + colour.g * a) / 255),
                        (byte)((had.b * (255 - a) + colour.b * a) / 255),
                        (byte)(had.a + (255 - had.a) * a / 255));
                }
            }
        }

        /// <summary>One pixel laid over, for the per-pixel passes (the glow, the
        /// shoreline's softer marks).</summary>
        public void OverDot(int x, int y, Color32 colour)
        {
            if (x < 0 || y < 0 || x >= RW || y >= RH || colour.a == 0)
                return;

            int at = y * RW + x;
            if (colour.a == 255)
            {
                _pixels[at] = colour;
                return;
            }

            int a = colour.a;
            var had = _pixels[at];
            _pixels[at] = new Color32(
                (byte)((had.r * (255 - a) + colour.r * a) / 255),
                (byte)((had.g * (255 - a) + colour.g * a) / 255),
                (byte)((had.b * (255 - a) + colour.b * a) / 255),
                (byte)(had.a + (255 - had.a) * a / 255));
        }

        // ---------------------------------------------------------- authored ink

        /// <summary>A block of city, in AUTHORED units. Rounded to the raster the same
        /// way every time, and never allowed to vanish: a footprint under a third of a
        /// unit still gets its pixel, or a row of sheds would come out as a gap.</summary>
        public void Fill(float x, float y, float w, float h, Color32 colour) =>
            Px(Mathf.RoundToInt(x * S), Mathf.RoundToInt(y * S),
                Mathf.Max(1, Mathf.RoundToInt(w * S)), Mathf.Max(1, Mathf.RoundToInt(h * S)),
                colour);

        public void Fill(Rect authored, Color32 colour) =>
            Fill(authored.x, authored.y, authored.width, authored.height, colour);

        // ------------------------------------------------------------- the sheet

        public void Clear(Color32 colour)
        {
            // The moving layer is cleared every frame, and a managed loop over half a
            // million structs is a millisecond of it. Clearing to transparent black is
            // all-zero bytes, which Array.Clear does as a memset.
            if (colour.r == 0 && colour.g == 0 && colour.b == 0 && colour.a == 0)
            {
                System.Array.Clear(_pixels, 0, _pixels.Length);
                return;
            }

            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = colour;
        }

        public void Apply(Texture2D texture)
        {
            // SetPixelData and not SetPixels32: the buffer is already RGBA32 in the
            // order the texture wants it, so this is a memcpy where SetPixels32 is a
            // per-pixel marshal. On the layer that is uploaded every frame that
            // difference is most of the upload.
            texture.SetPixelData(_pixels, 0);
            texture.Apply(false);
        }

        /// <summary>Box-filter this plate into a smaller upload buffer. The corner map
        /// occupies 256 x 160 screen pixels and gains nothing from uploading all 960 x
        /// 600 source pixels; doing this on its worker keeps the main-thread handoff
        /// small without changing the shared survey renderer.</summary>
        public void Downsample(int factor, Color32[] into)
        {
            factor = Mathf.Max(1, factor);
            int wide = RW / factor, tall = RH / factor;
            if (into == null || into.Length != wide * tall)
                throw new System.ArgumentException("Downsample buffer has the wrong size.", nameof(into));

            int area = factor * factor;
            for (int y = 0; y < tall; y++)
                for (int x = 0; x < wide; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    int sourceY = y * factor;
                    int sourceX = x * factor;
                    for (int py = 0; py < factor; py++)
                    {
                        int at = (sourceY + py) * RW + sourceX;
                        for (int px = 0; px < factor; px++)
                        {
                            var colour = _pixels[at + px];
                            r += colour.r; g += colour.g; b += colour.b; a += colour.a;
                        }
                    }
                    into[y * wide + x] = new Color32(
                        (byte)(r / area), (byte)(g / area),
                        (byte)(b / area), (byte)(a / area));
                }
        }

        /// <summary>
        /// The three static layers flattened into one sheet, the way the screen stacks
        /// them: ground, the turf wash MULTIPLIED over it, then the footprints laid on
        /// top. The screen gets that stacking from three RawImages and a multiply
        /// material; anything that has only one texture to give - the corner minimap -
        /// has to be handed the same result already mixed.
        /// </summary>
        public void Compose(TurfPlate ground, TurfPlate wash, TurfPlate built)
        {
            for (int i = 0; i < _pixels.Length; i++)
            {
                var under = ground._pixels[i];

                var film = wash._pixels[i];
                if (film.a > 0)
                    under = new Color32(
                        (byte)(under.r * film.r / 255),
                        (byte)(under.g * film.g / 255),
                        (byte)(under.b * film.b / 255),
                        255);

                var over = built._pixels[i];
                if (over.a == 255)
                    under = over;
                else if (over.a > 0)
                    under = new Color32(
                        (byte)((under.r * (255 - over.a) + over.r * over.a) / 255),
                        (byte)((under.g * (255 - over.a) + over.g * over.a) / 255),
                        (byte)((under.b * (255 - over.a) + over.b * over.a) / 255),
                        255);

                _pixels[i] = under;
            }
        }

        /// <summary>Build the full turf sheet and its no-turf companion in one worker
        /// pass. The full map can then toggle the overlay while still uploading only
        /// one ready texture on the main thread.</summary>
        public void ComposePair(TurfPlate ground, TurfPlate wash, TurfPlate built,
                                TurfPlate withoutWash)
        {
            if (withoutWash == null)
                throw new System.ArgumentNullException(nameof(withoutWash));
            for (int i = 0; i < _pixels.Length; i++)
            {
                Color32 plain = ground._pixels[i];
                Color32 coloured = plain;

                var film = wash._pixels[i];
                if (film.a > 0)
                    coloured = new Color32(
                        (byte)(coloured.r * film.r / 255),
                        (byte)(coloured.g * film.g / 255),
                        (byte)(coloured.b * film.b / 255), 255);

                var over = built._pixels[i];
                if (over.a == 255)
                {
                    plain = over;
                    coloured = over;
                }
                else if (over.a > 0)
                {
                    plain = CompositeOver(plain, over);
                    coloured = CompositeOver(coloured, over);
                }

                _pixels[i] = coloured;
                withoutWash._pixels[i] = plain;
            }
        }

        static Color32 CompositeOver(Color32 under, Color32 over) => new Color32(
            (byte)((under.r * (255 - over.a) + over.r * over.a) / 255),
            (byte)((under.g * (255 - over.a) + over.g * over.a) / 255),
            (byte)((under.b * (255 - over.a) + over.b * over.a) / 255), 255);

        /// <summary>A plate-shaped texture: point filtered, clamped, no mips. Every
        /// layer of the map is one of these, and the pixelated upscale the design
        /// asks for is entirely this filter mode - nothing in the shader.</summary>
        public static Texture2D NewTexture(string name)
        {
            var texture = new Texture2D(RW, RH, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            return texture;
        }

        // ------------------------------------------------------------ the roll

        /// <summary>
        /// The plate's own random number generator - the same 32-bit LCG the reference
        /// design draws with, so a seed produces the same stipple, the same trees and
        /// the same wave marks in every build. A survey plate that shuffles its own
        /// texture between runs is not a document, and a screenshot of it proves
        /// nothing.
        /// </summary>
        public struct Roll
        {
            uint _state;

            public Roll(int seed) => _state = (uint)seed;

            /// <summary>0 inclusive to 1 exclusive.</summary>
            public float Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return _state / 4294967296f;
            }

            /// <summary>0 inclusive to <paramref name="count"/> exclusive.</summary>
            public int Next(int count) => Mathf.Min(count - 1, (int)(Next() * count));

            public bool Chance(float odds) => Next() < odds;
        }
    }
}
