using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>Unweighted admission to a set of lane destinations, including lane changes.</summary>
    public static class RoadReachability
    {
        /// <summary>Admission to many candidate destinations in one traversal, including lane changes.</summary>
        public static HashSet<RoadEdge> From(RoadEdge source)
        {
            var found = new HashSet<RoadEdge>();
            var pending = new Queue<RoadEdge>();
            void Add(RoadEdge lane) { if (lane != null && found.Add(lane)) pending.Enqueue(lane); }
            Add(source);
            while (pending.Count > 0)
            {
                var lane = pending.Dequeue();
                if (lane.Road != null)
                    foreach (var sibling in lane.Road.Lanes)
                        if (sibling.Heading == lane.Heading) Add(sibling);
                if (lane.To != null)
                    foreach (var next in lane.To.Outgoing)
                        if (lane.To.ConnectorFor(lane, next) != null) Add(next);
            }
            return found;
        }

    }
}
