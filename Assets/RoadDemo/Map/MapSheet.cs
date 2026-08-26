using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where the 320x200 sheet is held over the city: the ground under its middle, and
    /// how many metres of that ground one pixel is worth.
    ///
    /// The design sheet pins the scale at 1 px = 8 m and never moves it. This map does
    /// move it, on purpose and by decision: the city's map has always ridden the
    /// camera's own boom, so pulling the wheel back shows more ground rather than
    /// bigger pixels, and giving that up would have been a worse map than the one the
    /// project already had. Everything ELSE the sheet demands is kept exactly - the
    /// raster is 320x200 whatever the display is, every fill is a whole pixel, and the
    /// blow-up is nearest-neighbour.
    ///
    /// What floats with the scale is what the boom asks for: about a metre to the pixel
    /// as the plan comes up over the street, and about eleven with the whole city in
    /// the frame - so the sheet's own 8 m sits near the far end of the wheel, and a
    /// building pulled right back is the one or two pixels the design drew it as.
    ///
    /// The one cost of a floating scale is that the cached layers go stale on a ZOOM as
    /// well as on a pan, which is why <see cref="Matches"/> exists and why every bake
    /// is culled to <see cref="Window"/>.
    ///
    /// Raster coordinates run the way a screen does and not the way the world does: x
    /// east, y DOWN the sheet, so north is row zero.
    /// </summary>
    public readonly struct MapSheet
    {
        /// <summary>World XZ under the middle pixel of the sheet.</summary>
        public readonly Vector2 Centre;

        /// <summary>Metres of ground to one pixel.</summary>
        public readonly float Metres;

        public MapSheet(Vector2 centre, float metres)
        {
            Centre = centre;
            Metres = Mathf.Max(0.02f, metres);
        }

        public float PixelsPerMetre => 1f / Metres;

        /// <summary>The ground the sheet covers.</summary>
        public Rect Window => new Rect(
            Centre.x - MapRaster.W * 0.5f * Metres,
            Centre.y - MapRaster.H * 0.5f * Metres,
            MapRaster.W * Metres, MapRaster.H * Metres);

        /// <summary>The ground it covers with a margin, for culling things whose ink
        /// spills past their own footprint.</summary>
        public Rect Margin(float pixels)
        {
            var window = Window;
            var pad = pixels * Metres;
            return Rect.MinMaxRect(window.xMin - pad, window.yMin - pad,
                                   window.xMax + pad, window.yMax + pad);
        }

        /// <summary>A world point in raster coordinates - fractional, because hit
        /// testing works in these and wants the precision. Anything on its way INTO the
        /// buffer rounds first.</summary>
        public Vector2 ToPx(Vector2 world) => new Vector2(
            MapRaster.W * 0.5f + (world.x - Centre.x) / Metres,
            MapRaster.H * 0.5f - (world.y - Centre.y) / Metres);

        public Vector2 ToPx(Vector3 world) => ToPx(new Vector2(world.x, world.z));

        /// <summary>And back: the ground under a raster pixel.</summary>
        public Vector2 ToWorld(Vector2 px) => new Vector2(
            Centre.x + (px.x - MapRaster.W * 0.5f) * Metres,
            Centre.y - (px.y - MapRaster.H * 0.5f) * Metres);

        /// <summary>
        /// A world rectangle as whole pixels. Both edges are rounded rather than one
        /// floored and the other ceiled, because a footprint has to keep its SHAPE: a
        /// row of identical sheds must not come out as alternating widths. Never
        /// smaller than one pixel - a building the sheet cannot resolve is still a
        /// building, and dropping it would leave a hole in a street.
        /// </summary>
        public RectInt Box(Rect world)
        {
            var x0 = Mathf.RoundToInt(MapRaster.W * 0.5f + (world.xMin - Centre.x) / Metres);
            var x1 = Mathf.RoundToInt(MapRaster.W * 0.5f + (world.xMax - Centre.x) / Metres);
            // yMax is NORTH, which is the top of the sheet and therefore the low row.
            var y0 = Mathf.RoundToInt(MapRaster.H * 0.5f - (world.yMax - Centre.y) / Metres);
            var y1 = Mathf.RoundToInt(MapRaster.H * 0.5f - (world.yMin - Centre.y) / Metres);
            return new RectInt(x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0));
        }

        /// <summary>Whether a world rectangle has any business being drawn at all.</summary>
        public bool Sees(Rect world)
        {
            var window = Window;
            return world.xMax >= window.xMin && world.xMin <= window.xMax &&
                   world.yMax >= window.yMin && world.yMin <= window.yMax;
        }

        /// <summary>Whether a cached layer baked for <paramref name="other"/> still
        /// stands. Sub-pixel movement does not count: the camera drifts constantly and
        /// re-rasterising a city for a tenth of a pixel is work for nobody.</summary>
        public bool Matches(MapSheet other)
        {
            if (!Mathf.Approximately(Metres, other.Metres))
                return false;
            var moved = (Centre - other.Centre) / Metres;
            return Mathf.Abs(moved.x) < 0.5f && Mathf.Abs(moved.y) < 0.5f;
        }
    }
}
