using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Ambient traffic placement and route choice, shared by city and focused scenes.</summary>
    public static class TrafficDistribution
    {
        public readonly struct Slot
        {
            public readonly RoadEdge Lane;
            public readonly float Progress;
            public Slot(RoadEdge lane, float progress) { Lane = lane; Progress = progress; }
        }

        static bool Through(RoadEdge lane)
        {
            var node = lane?.To;
            if (node == null) return false;
            if (node.Connectors.Count > 0)
            {
                foreach (var c in node.Connectors) if (c.From == lane && !c.UTurn) return true;
                return false;
            }
            // Legacy composition may prepare connectors lazily.
            foreach (var exit in node.Outgoing)
                if (Vector3.Dot(lane.Dir, exit.Dir) >= -.5f) return true;
            return false;
        }

        /// <summary>Spread the requested population over usable lane capacity, then
        /// along each lane. A private seed leaves the body's random stream untouched.</summary>
        public static List<Slot> Place(IList<RoadEdge> edges, int count, int seed,
            float spacing = 18f, float margin = 12f)
        {
            var result = new List<Slot>();
            if (edges == null || count <= 0 || spacing <= 0f || margin < 0f) return result;
            var through = new List<RoadEdge>();
            var terminal = new List<RoadEdge>();
            var seen = new HashSet<RoadEdge>();
            foreach (var lane in edges)
                if (lane != null && !lane.Auxiliary && lane.Length >= 2f * margin && seen.Add(lane))
                    (Through(lane) ? through : terminal).Add(lane);
            var dice = new System.Random(seed);
            // Use terminal capacity only after the connected streets are full.
            // This also serves rigs made entirely of terminal roads.
            PlaceLanes(through, count, dice, spacing, margin, result);
            PlaceLanes(terminal, count - result.Count, dice, spacing, margin, result);
            if (result.Count < count)
                Debug.LogWarning($"[Traffic] Placed {result.Count}/{count} cars: eligible lanes are at spawn capacity.");
            return result;
        }

        static void PlaceLanes(List<RoadEdge> lanes, int count, System.Random dice,
            float spacing, float margin, List<Slot> result)
        {
            if (count <= 0 || lanes.Count == 0) return;
            for (int i = lanes.Count - 1; i > 0; i--)
            {
                int j = dice.Next(i + 1);
                (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
            }
            int capacity = 0;
            foreach (var lane in lanes) capacity += Capacity(lane, spacing, margin);
            count = Mathf.Min(count, capacity);
            int offset = 0, sample = 0;
            foreach (var lane in lanes)
            {
                int end = offset + Capacity(lane, spacing, margin), quota = 0;
                while (sample < count && (sample + .5) * capacity / count < end)
                { quota++; sample++; }
                offset = end;
                if (quota == 0) continue;
                float cell = (lane.Length - 2f * margin + spacing) / quota;
                float slack = Mathf.Max(0f, cell - spacing);
                for (int i = 0; i < quota; i++)
                {
                    float jitter = ((float)dice.NextDouble() - .5f) * slack * .4f;
                    result.Add(new Slot(lane, margin + slack * .5f + i * cell + jitter));
                }
            }
        }

        static int Capacity(RoadEdge lane, float spacing, float margin) =>
            Mathf.FloorToInt((lane.Length - 2f * margin) / spacing) + 1;

        /// <summary>Later service spawns use actual lane occupancy; distributed
        /// ambient cars no longer form a solid prefix of occupied spawn slots.</summary>
        public static float FreeSlot(RoadEdge lane)
        {
            if (lane?.Road == null || lane.Auxiliary) return -1f;
            for (float progress = 12f; progress <= lane.Length - 12f; progress += 18f)
            {
                float station = lane.RoadS(progress);
                if (!lane.Road.Busy(null, station - 9f, station + 9f,
                    lane.Offset - 1.5f, lane.Offset + 1.5f)) return progress;
            }
            return -1f;
        }

        static float Weight(RoadEdge lane, float preference)
        {
            if (lane == null) return 0f;
            float occupied = 0f;
            if (lane.Road != null)
                foreach (var body in lane.Road.Occupants)
                {
                    if (!body.Overlaps(lane.Offset - 1.2f, lane.Offset + 1.2f)) continue;
                    // Kerb furniture off the running lane is not a queue.
                    if (body.Parked && !body.BodyOverlaps(lane.Offset - 1.2f, lane.Offset + 1.2f)) continue;
                    occupied += Mathf.Max(4f, body.Length) + 3f;
                }
            // A red-light queue must not outweigh the terminal-spur penalty.
            // Retain some spur visits so a small ring does not carry every car.
            float pressure = Mathf.Min(2f, 1f + 6f * occupied / Mathf.Max(12f, lane.Length));
            return preference * (Through(lane) ? 1f : .08f) / (pressure * pressure);
        }

        // Road simulation and route choice run serially, like RoadCar's shared
        // left/right scratch lists. Reuse storage without rescanning road occupants.
        static readonly List<(RoadEdge Lane, float Weight)> choices = new List<(RoadEdge, float)>(8);

        /// <summary>Normalize only the choices that exist. At a map edge, the missing
        /// straight must not donate its probability to a turn along the boundary.</summary>
        public static RoadEdge Choose(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights, float roll)
        {
            choices.Clear();
            float total = 0f;
            void Add(RoadEdge lane, float preference)
            {
                if (lane == null) return;
                float weight = Weight(lane, preference);
                choices.Add((lane, weight));
                total += weight;
            }
            Add(straight, .55f);
            foreach (var lane in lefts) Add(lane, .2f / lefts.Count);
            foreach (var lane in rights) Add(lane, .25f / rights.Count);
            float pick = Mathf.Clamp01(roll) * total;
            foreach (var choice in choices)
            {
                pick -= choice.Weight;
                if (pick < 0f) return choice.Lane;
            }
            return choices.Count > 0 ? choices[choices.Count - 1].Lane : null;
        }
    }
}
