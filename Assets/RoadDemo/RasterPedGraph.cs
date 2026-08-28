using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The pavement graph implied by a <see cref="CoreRoads.Raster"/>.  It is the
    /// pedestrian counterpart of <see cref="RasterGraph"/>: one ring round every
    /// junction, two walks down every road, and a cap where a road ends.
    ///
    /// Junction crossings are marked as gated even before a signal is installed.
    /// That makes the shared crowd/traffic code recognise them as carriageway while
    /// leaving them usable (PedestrianAgent permits a gated link with no signal).
    /// </summary>
    public static class RasterPedGraph
    {
        const float WalkY = 0.1f;
        const float PavementHalf = 2.5f;

        enum Corner { NE, NW, SW, SE }

        sealed class Junction
        {
            public readonly PedNode[] Corners = new PedNode[4];
            public bool North, East, South, West;
        }

        public static List<PedLink> Build(CoreRoads.Raster raster, DistrictFrame frame)
        {
            var links = new List<PedLink>();
            if (raster == null) return links;

            var junctions = new Junction[raster.Junctions.Count];
            for (int i = 0; i < junctions.Length; i++)
            {
                var box = raster.Junctions[i];
                var junction = junctions[i] = new Junction();
                junction.Corners[(int)Corner.NE] = Node(frame, box.xMax + PavementHalf, box.yMax + PavementHalf);
                junction.Corners[(int)Corner.NW] = Node(frame, box.xMin - PavementHalf, box.yMax + PavementHalf);
                junction.Corners[(int)Corner.SW] = Node(frame, box.xMin - PavementHalf, box.yMin - PavementHalf);
                junction.Corners[(int)Corner.SE] = Node(frame, box.xMax + PavementHalf, box.yMin - PavementHalf);
            }

            // Which mouth of each box really carries a road. NodeA is the low
            // (south/west) end of a stretch and NodeB the high (north/east) end.
            foreach (var stretch in raster.Stretches)
            {
                if (stretch.Vertical)
                {
                    if (stretch.NodeA >= 0) junctions[stretch.NodeA].North = true;
                    if (stretch.NodeB >= 0) junctions[stretch.NodeB].South = true;
                }
                else
                {
                    if (stretch.NodeA >= 0) junctions[stretch.NodeA].East = true;
                    if (stretch.NodeB >= 0) junctions[stretch.NodeB].West = true;
                }
            }

            // Four sides of a junction ring. A side across a live mouth is a zebra;
            // a side at a closed mouth is ordinary cap pavement.
            foreach (var junction in junctions)
            {
                Pair(junction.Corners[(int)Corner.NW], junction.Corners[(int)Corner.NE],
                    junction.North, true, links);
                Pair(junction.Corners[(int)Corner.NE], junction.Corners[(int)Corner.SE],
                    junction.East, false, links);
                Pair(junction.Corners[(int)Corner.SE], junction.Corners[(int)Corner.SW],
                    junction.South, true, links);
                Pair(junction.Corners[(int)Corner.SW], junction.Corners[(int)Corner.NW],
                    junction.West, false, links);
            }

            foreach (var stretch in raster.Stretches)
            {
                float half = stretch.Width * CoreRoads.Cell * 0.5f + PavementHalf;
                if (stretch.Vertical)
                {
                    PedNode eastA, westA, eastB, westB;
                    if (stretch.NodeA >= 0)
                    {
                        eastA = junctions[stretch.NodeA].Corners[(int)Corner.NE];
                        westA = junctions[stretch.NodeA].Corners[(int)Corner.NW];
                    }
                    else
                    {
                        eastA = Node(frame, stretch.Crown + half, stretch.From);
                        westA = Node(frame, stretch.Crown - half, stretch.From);
                        Pair(eastA, westA, false, false, links);
                    }

                    if (stretch.NodeB >= 0)
                    {
                        eastB = junctions[stretch.NodeB].Corners[(int)Corner.SE];
                        westB = junctions[stretch.NodeB].Corners[(int)Corner.SW];
                    }
                    else
                    {
                        eastB = Node(frame, stretch.Crown + half, stretch.To);
                        westB = Node(frame, stretch.Crown - half, stretch.To);
                        Pair(eastB, westB, false, false, links);
                    }

                    Pair(eastA, eastB, false, false, links);
                    Pair(westA, westB, false, false, links);
                }
                else
                {
                    PedNode northA, southA, northB, southB;
                    if (stretch.NodeA >= 0)
                    {
                        northA = junctions[stretch.NodeA].Corners[(int)Corner.NE];
                        southA = junctions[stretch.NodeA].Corners[(int)Corner.SE];
                    }
                    else
                    {
                        northA = Node(frame, stretch.From, stretch.Crown + half);
                        southA = Node(frame, stretch.From, stretch.Crown - half);
                        Pair(northA, southA, false, false, links);
                    }

                    if (stretch.NodeB >= 0)
                    {
                        northB = junctions[stretch.NodeB].Corners[(int)Corner.NW];
                        southB = junctions[stretch.NodeB].Corners[(int)Corner.SW];
                    }
                    else
                    {
                        northB = Node(frame, stretch.To, stretch.Crown + half);
                        southB = Node(frame, stretch.To, stretch.Crown - half);
                        Pair(northB, southB, false, false, links);
                    }

                    Pair(northA, northB, false, false, links);
                    Pair(southA, southB, false, false, links);
                }
            }

            return links;
        }

        static PedNode Node(DistrictFrame frame, float x, float z) =>
            new PedNode { Pos = frame.ToWorld(new Vector3(x, WalkY, z)) };

        static void Pair(PedNode a, PedNode b, bool gated, bool blocksNorthSouth,
                         List<PedLink> into)
        {
            if (a == null || b == null || a == b) return;
            float length = (b.Pos - a.Pos).magnitude;
            if (length < 0.01f) return;
            var ab = new PedLink
            {
                From = a, To = b, Length = length, Gated = gated,
                BlocksNorthSouth = blocksNorthSouth,
            };
            var ba = new PedLink
            {
                From = b, To = a, Length = length, Gated = gated,
                BlocksNorthSouth = blocksNorthSouth,
            };
            a.Links.Add(ab);
            b.Links.Add(ba);
            into.Add(ab);
            into.Add(ba);
        }
    }
}
