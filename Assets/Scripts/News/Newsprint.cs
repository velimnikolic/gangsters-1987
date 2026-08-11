using System;

namespace LivingCity.News
{
    /// <summary>
    /// The math of a 1987 press photo: luminance in, ink-or-paper out, through a 45-degree
    /// halftone dot screen with a touch of grain. Engine-free on purpose - PortraitStudio
    /// runs this over its pixels at develop time, and the headless suite proofs the screen's
    /// properties (range, monotonicity, dot structure) without an Editor.
    ///
    /// Everything is a pure function of (luminance, x, y), so the same photograph always
    /// develops to the same print.
    /// </summary>
    public static class Newsprint
    {
        // The page's two inks, shared with any UI that wants to match the paper around
        // a photo. Warm off-white stock, not-quite-black ink - pure #000 on #FFF reads
        // as a screenshot, not a newspaper.
        public const float PaperR = 0.91f, PaperG = 0.88f, PaperB = 0.79f;
        public const float InkR = 0.11f, InkG = 0.10f, InkB = 0.09f;

        /// <summary>What transparent pixels develop as - the studio's cut-out subjects
        /// (guns, cars) sit on bare paper in print.</summary>
        public const float PaperLuminance = 0.88f;

        /// <summary>Dot pitch in pixels along the screen's diagonal axes. Four keeps
        /// visible dots on a 256-print without dissolving a face into texture.</summary>
        const float CellSize = 4f;

        /// <summary>
        /// 0 is solid ink, 1 is bare paper. Blend the two ink constants by the result.
        /// </summary>
        public static float Shade(float luminance, int x, int y)
        {
            // Press photos run contrasty - flat mid-grays die on cheap paper - but
            // lifted, not just stretched: a curve centred on 0.5 laid so much ink into
            // the upper mid-tones that a lit face muddled into its own backdrop.
            var tone = (luminance - 0.5f) * 1.30f + 0.58f;

            // Film grain, deterministic per pixel.
            tone += (Hash(x, y) - 0.5f) * 0.08f;
            if (tone < 0f) tone = 0f;
            if (tone > 1f) tone = 1f;

            // The classic rotated screen: two cosine gratings along the 45-degree
            // diagonals make a dot lattice; comparing tone against it grows dots
            // with darkness exactly the way an amplitude-modulated screen does.
            var f = (float)(Math.PI * 2.0) / (CellSize * 2f);
            var screen = 0.5f + 0.25f * ((float)Math.Cos((x + y) * f) + (float)Math.Cos((x - y) * f));

            // A soft threshold instead of a hard one: dots keep an edge but the
            // print doesn't shatter into noise at 256 pixels.
            var t = (tone - screen) * 3f + 0.5f;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return t;
        }

        /// <summary>Small integer hash to [0, 1) - grain with no System.Random, so
        /// develop order can never change a pixel.</summary>
        static float Hash(int x, int y)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
