using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The lane graph a <see cref="CoreRoads.Raster"/> implies: a node for every junction
    /// box, a carriageway down every stretch of road between two of them, and the lanes on
    /// it at the offsets the city uses.
    ///
    /// Lifted out of <see cref="CoreDistrict"/> when the industrial quarter turned out to
    /// need the identical thing (2026-08-26). It is worth keeping in one place because of
    /// what is in it: three faults the play harness found in the core, each of which cost a
    /// run to find and each of which any other quarter reading the same raster would have
    /// inherited whole.
    /// </summary>
    public static class RasterGraph
    {
        /// <summary>How far short of the tarmac's end a dead end's node stands.</summary>
        const float DeadEnd = 1f;

        /// <summary>The shortest stretch worth laying lanes down when one end of it is
        /// loose: a car and the room it stops in.</summary>
        const float ShortestLane = 12f;

        static readonly float[] Street = { 2.5f };
        static readonly float[] Boulevard = { 7.5f, 12.5f };
        static readonly float[] Alley = { 0f };

        /// <summary>
        /// Builds the graph, finished and ready to drive.
        ///
        /// <paramref name="frame"/> is where the quarter stands in the world - everything
        /// above this was laid in the quarter's own coordinates and the graph is built in
        /// the city's, which is the district contract's one crossing point.
        /// </summary>
        public static LaneNet Build(CoreRoads.Raster raster, DistrictFrame frame,
                                    float streetSpeed, float boulevardSpeed, float alleySpeed)
        {
            var net = new LaneNet();
            var nodes = new RoadNode[raster.Junctions.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                var box = frame.ToWorldRect(raster.Junctions[i]);
                nodes[i] = net.AddNode(box.center.x, box.center.y, box.width * 0.5f, box.height * 0.5f);
            }

            // a road that simply STOPS - at the edge of the quarter, or against a block's
            // face - still needs a node there. Without one its lane ends in mid air: a car
            // that reaches the end has no connector to take, so it stands where it stopped
            // for the rest of the run, and everything behind it stands too. A small box a
            // hair past the tarmac is what the other demos give a dead end, and LaneNet
            // builds the turn-round across it like any other way through a box.
            RoadNode End(Vector3 at) => net.AddNode(at.x, at.z, 0.5f, 0.5f, stopSetback: 0.5f);

            foreach (var reach in raster.Stretches)
            {
                // a stretch shorter than a car, hanging off the edge of the quarter with a
                // junction at its other end, carries no lanes. There is no room on it to
                // stop, let alone to turn round, and a car standing on one sticks out of
                // both ends into the boxes either side, where the junction cannot reason
                // about it - which is a pair of cars overlapping.
                bool loose = reach.NodeA < 0 || reach.NodeB < 0;
                if (loose && reach.To - reach.From < ShortestLane) continue;

                var a = frame.ToWorld(Along(reach, reach.From));
                var b = frame.ToWorld(Along(reach, reach.To));
                var along = (b - a).normalized;
                if (reach.NodeA < 0) a += along * DeadEnd;
                if (reach.NodeB < 0) b -= along * DeadEnd;
                var nodeA = reach.NodeA >= 0 ? nodes[reach.NodeA] : End(a - along * DeadEnd * 0.5f);
                var nodeB = reach.NodeB >= 0 ? nodes[reach.NodeB] : End(b + along * DeadEnd * 0.5f);
                // the quarter may be turned a quarter circle; a north-south road in its own
                // coordinates is an east-west one in the city's
                bool northSouth = Mathf.Abs(b.z - a.z) > Mathf.Abs(b.x - a.x);
                float half = reach.Width * CoreRoads.Cell * 0.5f;

                if (reach.Width == 1)
                {
                    // one way, and the way is the alley's own: against it, the carriageway
                    // is laid the other way round so its single lane runs where it should
                    if (reach.Direction < 0) net.AddOneWay(b, a, half, Alley, alleySpeed, nodeB, nodeA, northSouth);
                    else net.AddOneWay(a, b, half, Alley, alleySpeed, nodeA, nodeB, northSouth);
                    continue;
                }
                bool boulevard = reach.Width >= 7;
                net.AddRoad(a, b, half, boulevard ? Boulevard : Street,
                            boulevard ? boulevardSpeed : streetSpeed,
                            nodeA, nodeB, northSouth, boulevard ? 5f : 0f);
            }

            net.Finish();
            return net;
        }

        /// <summary>A point this far along a stretch of road, on its crown.</summary>
        static Vector3 Along(CoreRoads.Stretch reach, float along)
            => reach.Vertical ? new Vector3(reach.Crown, 0f, along) : new Vector3(along, 0f, reach.Crown);
    }
}
