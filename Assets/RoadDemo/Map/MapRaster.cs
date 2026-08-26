using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The 320x200 buffer everything on the tactical map is drawn into, and the rules
    /// that keep it a 1987 raster instead of a modern picture of one.
    ///
    /// There is exactly one way to put colour in here: whole pixels. No float rect ever
    /// reaches the buffer - every entry point rounds to integers at the door and clips
    /// against the edges - so nothing can be half-covered, which is what antialiasing
    /// IS. Alpha exists, but only as a wash over whole pixels (the turf layer): the
    /// result is still one exact colour per pixel and no edge is ever softened.
    ///
    /// The buffer is the size of the picture and never the size of the screen. What the
    /// display does with it is a nearest-neighbour blow-up in
    /// <see cref="TacticalHud"/>, and the only thing that changes when a window is
    /// dragged wider is how big a pixel comes out.
    ///
    /// Two kinds of raster use this class. The SCREEN one owns a texture and is the
    /// thing uploaded each frame; the CACHED ones - the base map, the buildings, the
    /// turf wash - are buffers with no texture at all, blitted into the screen raster
    /// in the drawing order the design sheet lays down. That is the whole performance
    /// story: a city of thousands of buildings is rasterised when its ownership or its
    /// framing changes, and a frame costs a memcpy plus the things that actually move.
    /// </summary>
    public sealed class MapRaster
    {
        /// <summary>The AUTHORED space. Structure - roads, pads, district rects,
        /// building footprints - is laid out in these units and multiplied by
        /// <see cref="S"/> on its way into the buffer. All hit-testing is in them too:
        /// a footprint is one or two authored units across and the tolerances that make
        /// it clickable only mean anything at that size.</summary>
        public const int AW = 320;
        public const int AH = 200;

        /// <summary>Real pixels to the authored unit.</summary>
        public const int S = 3;

        /// <summary>The real buffer. Structure stays chunky because it is authored
        /// coarse and blown up; dither, kerbs, lane markings, windows and every sprite
        /// are drawn at THIS resolution, which is what stops the map reading as mush.
        /// That split is the whole trick and it is worth stating twice.</summary>
        public const int W = AW * S;
        public const int H = AH * S;
        public const int Count = W * H;

        readonly Color32[] _px = new Color32[Count];
        Texture2D _tex;

        /// <summary>The raw buffer. Written directly by the terrain pass, which is a
        /// per-pixel sweep of the whole sheet and has no business going through a
        /// rectangle call three hundred and twenty times a row.</summary>
        public Color32[] Pixels => _px;

        /// <summary>The texture, made on first ask. Point filtered and unmipped: a
        /// mipmap is a blur by another name, and a bilinear sample would undo the whole
        /// exercise the moment the map is drawn at anything but 1:1.</summary>
        public Texture2D Texture
        {
            get
            {
                if (_tex == null)
                {
                    _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                    {
                        name = "Tactical Map",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0,
                    };
                }
                return _tex;
            }
        }

        public void Release()
        {
            if (_tex == null)
                return;
            Object.Destroy(_tex);
            _tex = null;
        }

        /// <summary>Push the buffer at the texture. Once a frame, never per draw.</summary>
        public void Apply()
        {
            Texture.SetPixels32(_px);
            Texture.Apply(false, false);
        }

        // ------------------------------------------------------------------ whole

        public void Clear(Color32 colour)
        {
            for (var i = 0; i < Count; i++)
                _px[i] = colour;
        }

        /// <summary>Take another buffer whole - the base blit, and the two cached
        /// layers over it.</summary>
        public void Blit(MapRaster source)
        {
            System.Array.Copy(source._px, _px, Count);
        }

        /// <summary>Take another buffer, skipping the pixels it left transparent. The
        /// cached building and turf layers are drawn on a clear ground and laid over
        /// the base this way, so each keeps its own dirty flag without any of them
        /// having to know what the others drew.</summary>
        public void Over(MapRaster source)
        {
            var src = source._px;
            for (var i = 0; i < Count; i++)
            {
                var c = src[i];
                if (c.a == 0)
                    continue;
                if (c.a == 255)
                {
                    _px[i] = c;
                    continue;
                }
                _px[i] = Mix(_px[i], c, c.a);
            }
        }

        // ------------------------------------------------------------------ pixels

        public void Px(int x, int y, Color32 colour)
        {
            if ((uint)x >= W || (uint)y >= H)
                return;
            _px[y * W + x] = colour;
        }

        public Color32 At(int x, int y) =>
            (uint)x >= W || (uint)y >= H ? default : _px[y * W + x];

        public void Fill(int x, int y, int w, int h, Color32 colour)
        {
            if (w <= 0 || h <= 0)
                return;
            var x0 = Mathf.Max(0, x);
            var y0 = Mathf.Max(0, y);
            var x1 = Mathf.Min(W, x + w);
            var y1 = Mathf.Min(H, y + h);
            for (var py = y0; py < y1; py++)
            {
                var row = py * W;
                for (var px = x0; px < x1; px++)
                    _px[row + px] = colour;
            }
        }

        public void Fill(RectInt area, Color32 colour) =>
            Fill(area.xMin, area.yMin, area.width, area.height, colour);

        /// <summary>A colour laid OVER what is there at a fraction of full strength -
        /// the turf wash and its stripes. Whole pixels only: this softens no edge, it
        /// only tints the inside of one.</summary>
        public void Wash(int x, int y, int w, int h, Color32 colour, float alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0f)
                return;
            var a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            if (a == 0)
                return;
            var x0 = Mathf.Max(0, x);
            var y0 = Mathf.Max(0, y);
            var x1 = Mathf.Min(W, x + w);
            var y1 = Mathf.Min(H, y + h);
            for (var py = y0; py < y1; py++)
            {
                var row = py * W;
                for (var px = x0; px < x1; px++)
                    _px[row + px] = Mix(_px[row + px], colour, a);
            }
        }

        public void WashPx(int x, int y, Color32 colour, byte alpha)
        {
            if ((uint)x >= W || (uint)y >= H)
                return;
            var i = y * W + x;
            _px[i] = Mix(_px[i], colour, alpha);
        }

        // ------------------------------------------------------------ layer buffers

        /// <summary>
        /// Composite a wash into a buffer that is itself a LAYER - one that will be laid
        /// over the base later by <see cref="Over"/> rather than drawn on the base now.
        /// Each pixel of such a buffer means "put this colour on whatever is under me at
        /// this strength", so laying a second wash on top of a first cannot simply blend
        /// the two colours: it has to work out the single colour-and-strength that comes
        /// to the same thing.
        ///
        /// Which it can, exactly. Two washes over an unknown ground B come to
        /// C2*a2 + C1*a1*(1-a2) + B*(1-a1)(1-a2), so the pair is one wash of strength
        /// 1-(1-a1)(1-a2) in the colour that leading bracket divides out to. That is
        /// what lets the turf layer carry a wash, a stripe and a cross-hatch in one
        /// cached buffer and still land on the map at the strengths the design asks for.
        /// </summary>
        public void LayerWash(int x, int y, int w, int h, Color32 colour, float alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0f)
                return;
            var x0 = Mathf.Max(0, x);
            var y0 = Mathf.Max(0, y);
            var x1 = Mathf.Min(W, x + w);
            var y1 = Mathf.Min(H, y + h);
            for (var py = y0; py < y1; py++)
                for (var px = x0; px < x1; px++)
                    Stack(py * W + px, colour, alpha);
        }

        public void LayerPx(int x, int y, Color32 colour, float alpha)
        {
            if ((uint)x >= W || (uint)y >= H || alpha <= 0f)
                return;
            Stack(y * W + x, colour, alpha);
        }

        /// <summary>An opaque mark in a layer buffer - a corner tag, a building.</summary>
        public void LayerFill(int x, int y, int w, int h, Color32 colour)
        {
            var opaque = new Color32(colour.r, colour.g, colour.b, 255);
            Fill(x, y, w, h, opaque);
        }

        void Stack(int i, Color32 over, float alpha)
        {
            var under = _px[i];
            var an = Mathf.Clamp01(alpha);
            var ae = under.a / 255f;
            var total = ae + an - ae * an;
            if (total <= 0.0001f)
                return;
            var keep = ae * (1f - an);
            _px[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.r * an + under.r * keep) / total), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.g * an + under.g * keep) / total), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.b * an + under.b * keep) / total), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(total * 255f), 0, 255));
        }

        // -------------------------------------------------------------- additive

        /// <summary>
        /// Light ADDED to what is there: dst = min(255, dst + src * alpha). The crews'
        /// bloom is made of these and nothing else - a stack of faint additive squares
        /// that brightens whatever it is standing on rather than covering it, so a dot
        /// over grass, concrete and asphalt reads the same on all three without being
        /// ringed in black to make it.
        /// </summary>
        public void AddRect(int x, int y, int w, int h, Color32 colour, float alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0f)
                return;
            var lit = Mathf.Clamp01(alpha);
            var r = (int)(colour.r * lit);
            var g = (int)(colour.g * lit);
            var b = (int)(colour.b * lit);
            if (r + g + b == 0)
                return;

            var x0 = Mathf.Max(0, x);
            var y0 = Mathf.Max(0, y);
            var x1 = Mathf.Min(W, x + w);
            var y1 = Mathf.Min(H, y + h);
            for (var py = y0; py < y1; py++)
            {
                var row = py * W;
                for (var px = x0; px < x1; px++)
                {
                    var under = _px[row + px];
                    _px[row + px] = new Color32(
                        (byte)Mathf.Min(255, under.r + r),
                        (byte)Mathf.Min(255, under.g + g),
                        (byte)Mathf.Min(255, under.b + b),
                        under.a);
                }
            }
        }

        // ------------------------------------------------------------- multiply

        /// <summary>
        /// A tint that darkens instead of greying: dst = dst * src / 255, per channel.
        /// The turf overlay is this and nothing else - streets, buildings and terrain
        /// stay fully legible underneath, exactly as a multiply layer leaves them,
        /// where an alpha wash would wash them out toward its own colour.
        /// </summary>
        public void MultiplyRun(int x, int y, int length, Color32 colour)
        {
            if (length <= 0 || (uint)y >= H)
                return;
            var x0 = Mathf.Max(0, x);
            var x1 = Mathf.Min(W, x + length);
            var row = y * W;
            for (var px = x0; px < x1; px++)
            {
                var under = _px[row + px];
                _px[row + px] = new Color32(
                    (byte)(under.r * colour.r / 255),
                    (byte)(under.g * colour.g / 255),
                    (byte)(under.b * colour.b / 255),
                    under.a);
            }
        }

        static Color32 Mix(Color32 under, Color32 over, byte alpha)
        {
            int a = alpha, inv = 255 - a;
            return new Color32(
                (byte)((over.r * a + under.r * inv) / 255),
                (byte)((over.g * a + under.g * inv) / 255),
                (byte)((over.b * a + under.b * inv) / 255),
                (byte)Mathf.Max(under.a, alpha));
        }

        // ------------------------------------------------------------------- edges

        /// <summary>A hollow rectangle, drawn INSIDE the rect it is given.</summary>
        public void Frame(int x, int y, int w, int h, int thick, Color32 colour)
        {
            if (w <= 0 || h <= 0 || thick <= 0)
                return;
            thick = Mathf.Min(thick, Mathf.Min(w, h));
            Fill(x, y, w, thick, colour);
            Fill(x, y + h - thick, w, thick, colour);
            Fill(x, y, thick, h, colour);
            Fill(x + w - thick, y, thick, h, colour);
        }

        /// <summary>A dashed run along X: <paramref name="on"/> pixels of paint every
        /// <paramref name="step"/>. The city's own road markings are 2 on, 3 off.</summary>
        public void HDash(int x, int y, int len, int on, int step, Color32 colour)
        {
            if (len <= 0 || step <= 0)
                return;
            for (var i = 0; i < len; i += step)
                Fill(x + i, y, Mathf.Min(on, len - i), 1, colour);
        }

        public void VDash(int x, int y, int len, int on, int step, Color32 colour)
        {
            if (len <= 0 || step <= 0)
                return;
            for (var i = 0; i < len; i += step)
                Fill(x, y + i, 1, Mathf.Min(on, len - i), colour);
        }

        /// <summary>The marching ants a district border is drawn as: a run of
        /// <paramref name="on"/> pixels every <paramref name="period"/> along each side,
        /// with the phase pushed round by <paramref name="phase"/> so a contested
        /// border crawls and an owned one stands still.</summary>
        public void MarchingFrame(int x, int y, int w, int h, int thick,
            int on, int period, int phase, Color32 colour)
        {
            if (w <= 0 || h <= 0 || period <= 0)
                return;
            for (var i = 0; i < w; i++)
            {
                if ((i + phase) % period >= on)
                    continue;
                Fill(x + i, y, 1, thick, colour);
                Fill(x + i, y + h - thick, 1, thick, colour);
            }
            for (var i = 0; i < h; i++)
            {
                if ((i + phase) % period >= on)
                    continue;
                Fill(x, y + i, thick, 1, colour);
                Fill(x + w - thick, y + i, thick, 1, colour);
            }
        }

        // ------------------------------------------------------------------ strips

        /// <summary>
        /// A road: a straight run of a given half-width between two points already in
        /// raster coordinates. Rasterised by testing every pixel of the run's bounding
        /// box against the segment, which is how a road drawn at any angle still comes
        /// out as whole pixels and never as a smeared diagonal.
        /// </summary>
        public void Strip(Vector2 a, Vector2 b, float half, Color32 colour)
        {
            if (half < 0.5f)
                half = 0.5f;

            var x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half);
            var x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half);
            var y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half);
            var y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half);
            if (x1 < 0 || y1 < 0 || x0 >= W || y0 >= H)
                return;

            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            x1 = Mathf.Min(W - 1, x1);
            y1 = Mathf.Min(H - 1, y1);

            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var lenSq = dx * dx + dy * dy;
            var limit = half * half;

            for (var py = y0; py <= y1; py++)
            {
                var row = py * W;
                for (var px = x0; px <= x1; px++)
                {
                    // The pixel's own middle against the segment - a pixel is in the
                    // road when its centre is, which is the integer-coverage rule.
                    var cx = px + 0.5f - a.x;
                    var cy = py + 0.5f - a.y;
                    var t = lenSq > 1e-6f ? Mathf.Clamp01((cx * dx + cy * dy) / lenSq) : 0f;
                    var ox = cx - dx * t;
                    var oy = cy - dy * t;
                    if (ox * ox + oy * oy <= limit)
                        _px[row + px] = colour;
                }
            }
        }

        /// <summary>The same run, dashed down its middle - a centre line.</summary>
        public void StripDash(Vector2 a, Vector2 b, int on, int step, Color32 colour)
        {
            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1f || step <= 0)
                return;
            dx /= len;
            dy /= len;
            for (var i = 0f; i < len; i += step)
                for (var k = 0; k < on && i + k < len; k++)
                    Px(Mathf.FloorToInt(a.x + dx * (i + k)),
                       Mathf.FloorToInt(a.y + dy * (i + k)), colour);
        }
    }
}
