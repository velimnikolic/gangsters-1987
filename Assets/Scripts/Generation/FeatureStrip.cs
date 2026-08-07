using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Decides whether one street-facing side of a block gives up its building row for a strip
    /// of parking or a pocket park.
    ///
    /// This is where the block's leftover length goes. A block is always a multiple of 30m and
    /// a terrace is always a sum of 8.1/8.9/11.4/13.1m pieces plus corners; the two have no
    /// common divisor, so a remainder always exists - up to a whole building's width - and for
    /// a long time it was spread through the street wall as gaps, where it read as broken
    /// geometry. Consolidated on one side of the block it reads as what a 1920s block actually
    /// had there: a surface car lot, or a paved corner plaza with benches and a kiosk. The
    /// building rows pack tight against it.
    ///
    /// Deterministic in (seed, blockId) alone, the same discipline as BlockLots and for the
    /// same reason: the strip SHRINKS the rectangle the lots are planned in, and BlockBuilder
    /// and GroundPlacer both plan those lots from their own rng streams. If the strip depended
    /// on either stream the two would disagree and the ground mosaic's seams would drift off
    /// the alleys.
    /// </summary>
    public static class FeatureStrip
    {
        /// <summary>
        /// Smallest block span, along the strip's axis, that must survive the strip - enough
        /// for a ringed lot with corner pieces. A block too small keeps its full perimeter.
        /// </summary>
        const float MinSpanAfter = 30f;

        /// <summary>Same stride as BlockLots, and for the reason documented there.</summary>
        const int BlockStride = 397;

        public struct Strip
        {
            public bool Has;
            public Sides Side;
            public float Width;

            /// <summary>Pocket park instead of parking - benches, trees, a kiosk.</summary>
            public bool IsPark;
        }

        /// <summary>
        /// The block's strip, or Has = false. <paramref name="enabled"/> is the palette's
        /// choice; both callers must pass the same expression for it, or they stop agreeing.
        /// A block with fewer than two street sides keeps its buildings - trading the only
        /// street elevation for parked cars would leave the block showing nothing but backs.
        /// </summary>
        public static Strip For(
            Vector2 min, Vector2 max, Sides roadSides, bool enabled,
            CityConfig config, int blockId)
        {
            var strip = new Strip();
            if (!enabled)
                return strip;

            var streets = 0;
            for (var i = 0; i < 4; i++)
                if (roadSides.HasFlag(SideAt(i)))
                    streets++;

            if (streets < 2)
                return strip;

            var rng = new System.Random(config.seed + SeedOffsets.FeatureStrips + blockId * BlockStride);

            // One draw per decision, in fixed order, so a config tweak to one range cannot
            // reshuffle the others.
            var pick = rng.Next(streets);
            var width = Mathf.Lerp(config.featureStripMin, config.featureStripMax,
                                   (float)rng.NextDouble());
            var isPark = rng.NextDouble() < config.pocketParkChance;

            var side = Sides.None;
            for (var i = 0; i < 4; i++)
            {
                if (!roadSides.HasFlag(SideAt(i)))
                    continue;
                if (pick-- == 0)
                {
                    side = SideAt(i);
                    break;
                }
            }

            var span = side is Sides.East or Sides.West ? max.x - min.x : max.y - min.y;
            if (span - width < MinSpanAfter)
                return strip;

            strip.Has = true;
            strip.Side = side;
            strip.Width = width;
            strip.IsPark = isPark;
            return strip;
        }

        /// <summary>Pulls one edge of the block rect in by the strip's width.</summary>
        public static void Shrink(in Strip strip, ref Vector2 min, ref Vector2 max)
        {
            if (!strip.Has)
                return;

            switch (strip.Side)
            {
                case Sides.West: min.x += strip.Width; break;
                case Sides.East: max.x -= strip.Width; break;
                case Sides.South: min.y += strip.Width; break;
                case Sides.North: max.y -= strip.Width; break;
            }
        }

        static Sides SideAt(int index) => index switch
        {
            0 => Sides.South,
            1 => Sides.East,
            2 => Sides.North,
            _ => Sides.West,
        };
    }
}
