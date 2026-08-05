using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Splits a block's buildable rectangle into lots.
    ///
    /// Lived inside BlockBuilder until GroundPlacer needed the same answer. That is not a
    /// convenience: the ground mosaic only reads as a city if its seams fall on the alleys
    /// between lots, which means both callers have to agree on the lot rectangles down to the
    /// centimetre.
    ///
    /// Sharing the function alone would not have been enough. SplitRun consumes rng draws, and
    /// BlockBuilder's stream has already spent an unknown number of them on the palette, the
    /// bleed palette and the landmark by the time it subdivides. GroundPlacer's stream is a
    /// different one entirely. So the plan is derived from its OWN per-block seed and nothing
    /// else - call it from anywhere, in any order, after any number of other draws, and the
    /// same block comes back with the same lots. It also means adding a ground to one palette
    /// can never move a building, which is the invariant GroundPlacer.PickGround already
    /// defends one level down.
    /// </summary>
    public static class BlockLots
    {
        /// <summary>
        /// Target size of one lot, and the number that decides how many smaller blocks a big
        /// block breaks into. The terrace kit is 13.4-15.7m deep, so at 42 a lot is just over
        /// two building depths and its front and rear rows stand back to back with nothing
        /// between them - which is what a Chicago block actually is.
        ///
        /// Against BlockRect's sizes it rounds to 1 lot at 46m (one cell), 2 at 76m, 3 at 106m.
        /// A one-cell block therefore stays a single ring and only the bigger ones split.
        /// </summary>
        public const float TargetLotSize = 42f;

        /// <summary>
        /// How far a single lot may deviate from an even share of its block. See SplitRun for
        /// why it is not zero and why it is not larger.
        /// </summary>
        const float LotSizeJitter = 0.25f;

        /// <summary>
        /// Spreads consecutive block ids apart in the seed space. A plain seed + blockId would
        /// make block 5 of seed 100 and block 4 of seed 101 the same block, and the mosaic would
        /// visibly repeat down a street whenever the seed was nudged by one.
        /// </summary>
        const int BlockStride = 397;

        public struct Lot
        {
            public Vector2 Min;
            public Vector2 Max;

            /// <summary>
            /// Whether each side faces a public street. False for an internal alley, and also
            /// false where the map boundary cuts the block off - both get the blank -back
            /// pieces, because neither has anyone walking past to show a shopfront to.
            /// </summary>
            public bool South, East, North, West;

            /// <summary>
            /// True when this lot IS the whole block. Only such a lot seals its courtyard on
            /// all four sides, so only it earns a passage through the street wall.
            /// </summary>
            public bool WholeBlock;
        }

        /// <summary>
        /// The lots of one block. Deterministic in (seed, blockId) alone - see the class note
        /// for why that matters more than it looks.
        /// </summary>
        public static List<Lot> Plan(
            Vector2 min,
            Vector2 max,
            Sides roadSides,
            int maxLotsPerAxis,
            float alleyWidth,
            int seed,
            int blockId)
        {
            var rng = new System.Random(seed + SeedOffsets.Lots + blockId * BlockStride);
            var lots = new List<Lot>();

            var width = max.x - min.x;
            var depth = max.y - min.y;

            var columns = Mathf.Max(1, Mathf.RoundToInt(width / TargetLotSize));
            var rows = Mathf.Max(1, Mathf.RoundToInt(depth / TargetLotSize));

            // Interior lots can only ever face the alleys, so a zone that wants every window
            // on a street caps the grid - at 1 the whole block is one ring round one yard.
            if (maxLotsPerAxis > 0)
            {
                columns = Mathf.Min(columns, maxLotsPerAxis);
                rows = Mathf.Min(rows, maxLotsPerAxis);
            }

            var runX = width - (columns - 1) * alleyWidth;
            var runZ = depth - (rows - 1) * alleyWidth;

            // Tested against the narrowest a lot can come out of SplitRun, not the average,
            // so the jitter cannot slip a degenerate lot past this.
            if (runX / columns * (1f - LotSizeJitter) <= 1f
             || runZ / rows * (1f - LotSizeJitter) <= 1f)
            {
                lots.Add(new Lot
                {
                    Min = min,
                    Max = max,
                    South = roadSides.HasFlag(Sides.South),
                    East = roadSides.HasFlag(Sides.East),
                    North = roadSides.HasFlag(Sides.North),
                    West = roadSides.HasFlag(Sides.West),
                    WholeBlock = true,
                });
                return lots;
            }

            var lotWidths = SplitRun(runX, columns, rng);
            var lotDepths = SplitRun(runZ, rows, rng);

            var x = min.x;
            for (var column = 0; column < columns; column++)
            {
                var z = min.y;
                for (var row = 0; row < rows; row++)
                {
                    lots.Add(new Lot
                    {
                        Min = new Vector2(x, z),
                        Max = new Vector2(x + lotWidths[column], z + lotDepths[row]),
                        // An outer lot only fronts a street if there is actually a street there:
                        // against the map boundary there is not, so it falls back to alley pieces.
                        South = row == 0 && roadSides.HasFlag(Sides.South),
                        North = row == rows - 1 && roadSides.HasFlag(Sides.North),
                        West = column == 0 && roadSides.HasFlag(Sides.West),
                        East = column == columns - 1 && roadSides.HasFlag(Sides.East),
                        WholeBlock = columns == 1 && rows == 1,
                    });

                    z += lotDepths[row] + alleyWidth;
                }

                x += lotWidths[column] + alleyWidth;
            }

            return lots;
        }

        /// <summary>
        /// Splits a run into <paramref name="count"/> UNEQUAL parts that still add up to it.
        ///
        /// Equal parts are the same defect the city layout itself used to have, one level down:
        /// the alleys line up into a grid of their own and the block reads as a spreadsheet.
        /// It barely showed while the largest block was two cells - at most two lots per axis -
        /// but CityGenerator.Subdivide now produces three-cell blocks, which are three lots
        /// across, and three evenly-spaced alleys is exactly what it looks like.
        ///
        /// The jitter is capped because a lot has to stay wide enough to wrap a terrace around,
        /// corners included: at +/-25% a third of a ~96m run comes out 24-40m rather than a flat
        /// 32m. At count 1 the single part is the whole run, so a one-lot block is unchanged.
        ///
        /// Public because GroundPlacer divides a yard into paving patches and wants exactly the
        /// same property one scale down: even patches read as a checkerboard, which is the same
        /// defect in a 30m yard that even lots are in a 100m block.
        /// </summary>
        public static float[] SplitRun(float total, int count, System.Random rng)
        {
            var parts = new float[count];
            var sum = 0f;

            for (var i = 0; i < count; i++)
            {
                parts[i] = 1f - LotSizeJitter + 2f * LotSizeJitter * (float)rng.NextDouble();
                sum += parts[i];
            }

            // Normalised rather than clamped, so the parts always tile the run exactly and no
            // rounding slack shows up as a sliver of bare ground at the far corner.
            for (var i = 0; i < count; i++)
                parts[i] = parts[i] / sum * total;

            return parts;
        }
    }
}
