using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The HUD's shared sprite skin: the "Waste No Space" 64x64 GUI sheet (flatus.itch.io,
    /// CC0 - free for commercial use, no attribution owed) sliced into 9-patch chrome at
    /// runtime. One texture in Resources, no sprite-editor slicing, no scene references -
    /// every rect below is copied from the pack's own Godot styleboxes, which are the
    /// author's statement of where the patches are.
    ///
    /// Two rules keep this safe to call from anywhere:
    /// - Everything degrades to nothing. If the sheet has not been imported yet (a fresh
    ///   checkout, an Editor that has not refreshed), TryDress returns false and the caller
    ///   keeps the solid-colour Image it built first - the HUD looks like it did before
    ///   this class existed, instead of drawing white squares.
    /// - The ledger book is NOT a customer. LedgerStyle's paper-and-desk look is an authored
    ///   look with a sprite-less discipline of its own; this skin covers the street-level
    ///   chrome (menus, popups, bars) and stops at the book's cover.
    ///
    /// The sheet's patches are 5x5 with a 2px frame. Drawn 1:1 on a 1080p reference canvas
    /// a 2px frame is a hairline, so sprites are created at 100/PixelScale pixels-per-unit:
    /// the Image's sliced border then lands at 2 * PixelScale reference pixels, which is
    /// the same 3x chunk the pack's own preview scene uses.
    /// </summary>
    public static class UiSkin
    {
        /// <summary>How many reference pixels one sheet pixel covers. 3 is the pack's own
        /// preview scale; the border math above is the only thing that reads it.</summary>
        public const float PixelScale = 3f;

        const float PixelsPerUnit = 100f / PixelScale;

        /// <summary>Sheet height in texels - Godot rects count y from the top, Unity from
        /// the bottom, and this is the flip term.</summary>
        const int SheetHeight = 64;

        static Texture2D sheet;
        static bool loadTried;

        // Panels. "Dark" is the pack's popup panel and reads as the default popup chrome;
        // "Raised" is its lighter framed panel for surfaces that sit on other surfaces.
        static Sprite panelDark;
        static Sprite panelRaised;
        static Sprite sunken;

        // The button in its four states - Unity's SpriteSwap transition wants exactly this
        // family, so a themed button costs no code beyond handing them over.
        static Sprite buttonNormal;
        static Sprite buttonHover;
        static Sprite buttonPressed;
        static Sprite buttonDisabled;

        /// <summary>The pack's popup panel - context menu, overlay popup, map card.</summary>
        public static Sprite PanelDark =>
            panelDark ??= Slice(7, 7, 5, 5, Frame(2));

        /// <summary>The lighter framed panel - the top clock bar.</summary>
        public static Sprite PanelRaised =>
            panelRaised ??= Slice(7, 25, 5, 5, Frame(2));

        /// <summary>The pressed/inset box - a bar's trough, anything that reads as a well
        /// something else sits in.</summary>
        public static Sprite Sunken =>
            sunken ??= Slice(1, 31, 5, 5, Frame(2));

        public static Sprite ButtonNormal =>
            buttonNormal ??= Slice(1, 25, 5, 5, Frame(2));

        public static Sprite ButtonHover =>
            buttonHover ??= Slice(1, 13, 5, 5, Frame(2));

        public static Sprite ButtonPressed =>
            buttonPressed ??= Slice(1, 31, 5, 5, Frame(2));

        public static Sprite ButtonDisabled =>
            buttonDisabled ??= Slice(1, 1, 5, 5, Frame(2));

        /// <summary>The inset a panel's frame takes from its content, in reference pixels -
        /// what a caller adds to its padding so text clears the drawn border.</summary>
        public const float FrameInset = 2f * PixelScale;

        /// <summary>
        /// Puts the sprite on the Image as sliced chrome. False - with the Image untouched -
        /// when the sheet is missing, so the caller's solid-colour fallback stays visible.
        /// The tint multiplies the sprite; the fill patches are near-white so a tint is how
        /// a bar gets its colour.
        /// </summary>
        public static bool TryDress(Image image, Sprite sprite, Color? tint = null)
        {
            if (!image || sprite == null)
                return false;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = tint ?? Color.white;
            return true;
        }

        /// <summary>
        /// Themes a Button: normal face on its Image, the other three states through
        /// SpriteSwap. False when the sheet is missing - the caller should keep its old
        /// ColorBlock tints in that case, because SpriteSwap without sprites is a button
        /// with no feedback at all.
        /// </summary>
        public static bool TryDressButton(Button button, Image image)
        {
            if (!button || !TryDress(image, ButtonNormal))
                return false;

            button.transition = Selectable.Transition.SpriteSwap;
            var states = button.spriteState;
            states.highlightedSprite = ButtonHover;
            states.selectedSprite = ButtonHover;
            states.pressedSprite = ButtonPressed;
            states.disabledSprite = ButtonDisabled;
            button.spriteState = states;
            return true;
        }

        // ------------------------------------------------------------------- stars
        // Not from the sheet: the pack has no star, and the ledger wants a real one -
        // a five-point gold star with a glint, baked once into a small texture. The
        // half star is baked too (left half lit, right half the empty style), so no
        // RectMask2D gymnastics survive at the call site.

        static Sprite starFull;
        static Sprite starHalf;
        static Sprite starEmpty;

        public static Sprite StarFull => starFull ??= BakeStar(1f);

        /// <summary>Lit on the left of centre, empty on the right.</summary>
        public static Sprite StarHalf => starHalf ??= BakeStar(0f);

        public static Sprite StarEmpty => starEmpty ??= BakeStar(-1f);

        /// <summary>Texel edge of the baked star. Rendered around 20 reference pixels,
        /// so 40 texels plus 3x3 supersampling keeps the points smooth.</summary>
        const int StarTexels = 40;
        const int StarSamples = 3;

        static readonly Color GoldTop = new Color(1f, 0.95f, 0.60f);
        static readonly Color GoldBottom = new Color(0.86f, 0.55f, 0.12f);
        static readonly Color GoldRim = new Color(0.48f, 0.29f, 0.05f);
        static readonly Color Glint = new Color(1f, 1f, 0.94f);
        static readonly Color EmptyFill = new Color(0.72f, 0.78f, 0.72f, 0.10f);
        static readonly Color EmptyRim = new Color(0.55f, 0.60f, 0.55f, 0.55f);

        /// <summary>litUpTo is a normalized x threshold: +1 = whole star gold, 0 = the
        /// left half, -1 = none. Supersampled point-in-polygon rasterisation - the rim
        /// is the band between the full star and the same star at 78%.</summary>
        static Sprite BakeStar(float litUpTo)
        {
            var texture = new Texture2D(StarTexels, StarTexels, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[StarTexels * StarTexels];
            var step = 2f / (StarTexels * StarSamples);

            for (var py = 0; py < StarTexels; py++)
            for (var px = 0; px < StarTexels; px++)
            {
                var sum = new Color(0f, 0f, 0f, 0f);
                for (var sy = 0; sy < StarSamples; sy++)
                for (var sx = 0; sx < StarSamples; sx++)
                {
                    var x = -1f + (px * StarSamples + sx + 0.5f) * step;
                    var y = -1f + (py * StarSamples + sy + 0.5f) * step;
                    sum += StarSample(x, y, litUpTo);
                }

                pixels[py * StarTexels + px] = sum / (StarSamples * StarSamples);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, StarTexels, StarTexels),
                new Vector2(0.5f, 0.5f), 100f);
        }

        static Color StarSample(float x, float y, float litUpTo)
        {
            if (!InStar(x, y, 1f))
                return new Color(0f, 0f, 0f, 0f);

            var lit = x < litUpTo;
            if (!InStar(x, y, 0.78f))
                return lit ? GoldRim : EmptyRim;

            if (!lit)
                return EmptyFill;

            // Vertical gradient plus one glint high on the left - the "shiny".
            var color = Color.Lerp(GoldBottom, GoldTop, (y + 1f) * 0.5f);
            var glintDistance = Mathf.Sqrt((x + 0.22f) * (x + 0.22f) +
                                           (y - 0.30f) * (y - 0.30f));
            if (glintDistance < 0.24f)
                color = Color.Lerp(Glint, color, glintDistance / 0.24f);
            return color;
        }

        /// <summary>Even-odd containment against the ten-vertex star polygon, point up.</summary>
        static bool InStar(float x, float y, float scale)
        {
            var inside = false;
            for (int i = 0, j = 9; i < 10; j = i++)
            {
                StarVertex(i, scale, out var xi, out var yi);
                StarVertex(j, scale, out var xj, out var yj);
                if (yi > y != yj > y &&
                    x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }

        static void StarVertex(int index, float scale, out float x, out float y)
        {
            var angle = (90f + index * 36f) * Mathf.Deg2Rad;
            var radius = (index % 2 == 0 ? 0.96f : 0.44f) * scale;
            x = Mathf.Cos(angle) * radius;
            y = Mathf.Sin(angle) * radius;
        }

        static Vector4 Frame(float thickness) =>
            new Vector4(thickness, thickness, thickness, thickness);

        /// <summary>
        /// One patch off the sheet. x/y/w/h are the Godot stylebox's region_rect - y counts
        /// from the sheet's top and is flipped here, once, instead of in every table entry.
        /// </summary>
        static Sprite Slice(int x, int yFromTop, int width, int height, Vector4 border)
        {
            var texture = Sheet;
            if (!texture)
                return null;

            var sprite = Sprite.Create(
                texture,
                new Rect(x, SheetHeight - yFromTop - height, width, height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        static Texture2D Sheet
        {
            get
            {
                if (sheet || loadTried)
                    return sheet;

                loadTried = true;
                sheet = Resources.Load<Texture2D>("WasteNoSpaceGui");
                if (!sheet)
                    Debug.LogWarning("[UiSkin] WasteNoSpaceGui.png is not imported yet - " +
                                     "the HUD falls back to its solid-colour chrome. " +
                                     "Focus the Editor once so the asset refresh runs.");
                return sheet;
            }
        }
    }
}
