using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// How much ground one park cell may plant, measured outward from its own centre. Pure
    /// geometry - this class instantiates nothing, the same discipline HedgeLayout and
    /// ParkingLayout are written under.
    ///
    /// It exists because the park's planting and the park's boundary stopped agreeing. Planting
    /// was bounded by the CELL, at CellHalf - EdgeInset (13.5), which was right while the hedge
    /// ran the cell boundary too. The hedge now runs the block line - 8m further out, level with
    /// the street walls around it, see HedgeLayout - and the apron under that band is the park's
    /// own grass. So a park that stopped planting at 13.5 left a 9.5m ring of bare lawn inside
    /// its own hedge, all the way round.
    ///
    /// Closing that ring is not a tidy-up. For a one-cell park the plantable square goes from
    /// 27x27 to about 43x43 - nearly three times the ground - which is most of where a park gets
    /// to look like woodland rather than like a lawn with an avenue on it.
    ///
    /// The limit is per DIRECTION, not one number, for the same reason BlockRect resolves its
    /// clearance per side: a cell can face the dual carriageway on one side and a side street on
    /// the others, and a park on the edge of the map faces no road at all on one of them.
    ///
    /// Where the park CONTINUES into another of its own cells the limit is the cell boundary
    /// exactly. Not because anything would be wrong 2m further on - the neighbour is park too -
    /// but because the neighbour plants that ground itself, and two cells both planting the strip
    /// between them is how a seam turns into a thicket.
    /// </summary>
    public static class ParkPlot
    {
        /// <summary>
        /// The four outward limits of one cell's plot, in cell-local metres. Held as four floats
        /// rather than a Rect because every consumer asks the same question - "may I plant at
        /// this offset" - and a Rect would invite someone to read a centre out of it that is not
        /// the cell centre.
        /// </summary>
        public readonly struct Extent
        {
            public readonly float East;
            public readonly float North;
            public readonly float West;
            public readonly float South;

            public Extent(float east, float north, float west, float south)
            {
                East = east;
                North = north;
                West = west;
                South = south;
            }

            /// <summary>
            /// Whether a cell-local offset is inside the plot. <paramref name="margin"/> is the
            /// room the thing being placed needs around that point - pass its radius and a wide
            /// crown stops hanging over the hedge instead of merely having its trunk inside.
            /// </summary>
            public bool Contains(Vector2 offset, float margin = 0f) =>
                offset.x + margin <= East &&
                offset.y + margin <= North &&
                -offset.x + margin <= West &&
                -offset.y + margin <= South;

            /// <summary>
            /// The limit in one of the four cardinal directions. Takes the direction as a vector
            /// because that is what the callers already have - ParkDresser walks the same four
            /// legs the tile's walks run along.
            /// </summary>
            public float Toward(Vector2 direction) =>
                direction.x > 0.5f ? East
                : direction.x < -0.5f ? West
                : direction.y > 0.5f ? North
                : South;

            /// <summary>The tightest of the four - the half-size of a square that fits whichever way it leans.</summary>
            public float Least => Mathf.Min(Mathf.Min(East, North), Mathf.Min(West, South));

            /// <summary>The loosest of the four, for a caller that wants to sample and reject.</summary>
            public float Greatest => Mathf.Max(Mathf.Max(East, North), Mathf.Max(West, South));
        }

        /// <summary>
        /// The plot for one cell.
        ///
        /// <paramref name="inset"/> is held back off the boundary on every side that has one, so
        /// a crown does not push through the hedge; it is NOT applied to an interior edge, where
        /// there is no boundary to push through and holding back would open a seam.
        ///
        /// Takes the two clearances rather than a CityConfig so it reads the same values
        /// BlockBuilder.ClearanceFor gives BlockRect and HedgeLayout gives the hedge - one line,
        /// three consumers, no drift.
        /// </summary>
        public static Extent For(
            CityGrid grid,
            HashSet<Vector2Int> inBlock,
            Vector2Int cell,
            float clearance,
            float mainClearance,
            float mapEdgeOffset,
            float inset)
        {
            var cellHalf = CityGrid.CellSize * 0.5f;

            float Limit(int dx, int dz)
            {
                var neighbour = new Vector2Int(cell.x + dx, cell.y + dz);
                if (inBlock != null && inBlock.Contains(neighbour))
                    return cellHalf;

                var boundary = HedgeLayout.SideOffset(
                    grid, neighbour.x, neighbour.y, clearance, mainClearance, mapEdgeOffset);

                return Mathf.Max(0f, boundary - inset);
            }

            return new Extent(Limit(1, 0), Limit(0, 1), Limit(-1, 0), Limit(0, -1));
        }
    }
}
