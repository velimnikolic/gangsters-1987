using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// MAY A BODY BE PUT ONTO A LANE HERE? The one question a police car swinging out of
    /// its bay and a commuter coming out of a car-park gate both ask, answered once. A
    /// body driven by hand onto the road is nobody's lead until it is on the graph, so
    /// the car coming along has nothing to brake for until the footprint is already in
    /// its lane; the gate has to refuse while anybody WOULD reach the point inside the
    /// seconds the manoeuvre takes, at the speed he is doing. Three places he can be:
    /// on the lane behind the point, crossing the junction into it (on no lane at all -
    /// the police car that met its yard-mate's tail coming out of the box, DEPOT-004 S2
    /// seed 101), or on a road that feeds that junction.
    /// </summary>
    public static class LaneGate
    {
        /// <summary>Metres of road allowed for the arriving car to stop in.</summary>
        const float Stopping = 8f;

        /// <summary>A junction whose connector is not known is crossed in about this.</summary>
        const float BoxGuess = 12f;

        /// <param name="lane">the lane the body goes onto</param>
        /// <param name="at">progress along it where the body meets the lane</param>
        /// <param name="seconds">how long the manoeuvre keeps the body across the lane</param>
        /// <param name="self">the car asking, ignored wherever it turns up</param>
        /// <param name="behindMin">metres behind the point refused whatever the speed -
        /// a car standing right there is in the way even at nought</param>
        /// <param name="aheadMax">metres past the point still counted as in the way</param>
        public static bool Clear(RoadEdge lane, float at, float seconds, RoadCar self,
                                 float behindMin = 14f, float aheadMax = 8f)
        {
            if (lane == null) return false;

            var cars = lane.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (ReferenceEquals(car, self)) continue;
                float ahead = at - car.Progress;          // metres before it reaches the point
                if (ahead < -aheadMax) continue;           // past it already
                if (ahead < Mathf.Max(behindMin, Reach(car, lane, seconds))) return false;
            }

            var from = lane.From;
            if (from == null) return true;

            // in the box, on the way onto this lane
            for (int i = 0; i < from.Inside.Count; i++)
            {
                var o = from.Inside[i];
                if (o == null || o.Car == null || ReferenceEquals(o.Car, self)) continue;
                if (o.Via == null || o.Via.To != lane) continue;
                float ahead = Mathf.Max(0f, o.Via.Length - o.S) + at;
                if (ahead < Reach(o.Car, lane, seconds)) return false;
            }

            // on the roads that feed the box
            for (int e = 0; e < from.Incoming.Count; e++)
            {
                var feed = from.Incoming[e];
                if (feed == lane) continue;
                float cross = CrossLength(from, feed, lane);
                for (int i = 0; i < feed.Cars.Count; i++)
                {
                    var car = feed.Cars[i];
                    if (ReferenceEquals(car, self)) continue;
                    float ahead = feed.Length - car.Progress + cross + at;
                    if (ahead < Reach(car, feed, seconds)) return false;
                }
            }
            return true;
        }

        /// <summary>How far this car may get in the manoeuvre's seconds, plus room to
        /// stop. AT THE ROAD'S SPEED, NOT ITS OWN: a car standing fourteen metres back
        /// is a car whose lead is about to move off - it stood there because of the
        /// last car out of this yard - and it was doing eight metres a second by the
        /// time the swing ended (DEPOT-004 S2 seed 105, two spawns into one civilian,
        /// 431 refusals). What it is doing now is a floor, never a ceiling.</summary>
        static float Reach(RoadCar car, RoadEdge road, float seconds)
        {
            float v = Mathf.Max(car.Speed, road != null ? road.SpeedLimit : 0f);
            return v * seconds + Stopping;
        }

        /// <summary>May a hand-driven body cross this junction's box for the next
        /// seconds? Nobody in it now, and nobody who would arrive in it in that time
        /// off any road that feeds it. A car-park gate on the corner of a junction
        /// sends its commuters in through the box, which no reservation knows about
        /// (DEPOT-004 S2 seed 105: a car straight through the box met one turning in
        /// to the gate, 57 refusals).</summary>
        public static bool BoxClear(RoadNode node, float seconds, RoadCar self)
        {
            if (node == null) return true;
            for (int i = 0; i < node.Inside.Count; i++)
            {
                var o = node.Inside[i];
                if (o == null || o.Car == null || ReferenceEquals(o.Car, self)) continue;
                return false;
            }
            for (int e = 0; e < node.Incoming.Count; e++)
            {
                var feed = node.Incoming[e];
                for (int i = 0; i < feed.Cars.Count; i++)
                {
                    var car = feed.Cars[i];
                    if (ReferenceEquals(car, self)) continue;
                    float ahead = feed.Length - car.Progress;
                    if (ahead < Reach(car, feed, seconds)) return false;
                }
            }
            return true;
        }

        static float CrossLength(RoadNode node, RoadEdge from, RoadEdge to)
        {
            for (int i = 0; i < node.Connectors.Count; i++)
            {
                var c = node.Connectors[i];
                if (c.From == from && c.To == to) return c.Length;
            }
            return BoxGuess;
        }
    }
}
