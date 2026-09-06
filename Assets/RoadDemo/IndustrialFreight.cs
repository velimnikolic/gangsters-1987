using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Recurring physical port/estate calls. RoadCar owns routing, parking and road claims.</summary>
    public sealed class IndustrialFreight : System.IDisposable
    {
        public readonly struct Stop
        {
            public readonly RoadEdge Lane;
            public readonly float S;
            public Stop(RoadEdge lane, float s) { Lane = lane; S = s; }
            public Vector3 Position => Lane.Road.Pose(S, Lane.Offset);
            // RoadCar may choose a free kerb within its 45 m parking search.
            public bool Arrived(RoadCar car) => car.AtGoal && car.ParkedAtKerb && car.Road == Lane.Road &&
                car.Heading == Lane.Heading && Mathf.Abs(car.S - S) <= 46f;
        }
        readonly RoadCar _vehicle;
        Stop _estate, _port;
        readonly List<Stop> _homes, _ports;
        readonly HashSet<Vector3> _portClaims;
        readonly RoadProgress _progress = new RoadProgress(120f, 2f);
        bool _toPort = true, _travelling, _disposed, _reported;
        int _failures;
        float _wait;
        public int CompletedCalls { get; private set; }

        IndustrialFreight(RoadCar vehicle, Stop estate, Stop port, List<Stop> homes, List<Stop> ports,
            HashSet<Vector3> claims, float delay)
        { _vehicle = vehicle; _estate = estate; _port = port; _homes = homes; _ports = ports; _portClaims = claims; _wait = delay; }

        public static IndustrialFreight TryCreate(RoadCar vehicle, LaneNet net, Rect estate, Rect port, int index, HashSet<Vector3> portClaims)
        {
            if (net == null || vehicle == null || vehicle.Gone || vehicle.Derelict || vehicle.Wrecked ||
                vehicle.CurrentEdge == null || portClaims == null || index < 0) return null;
            var home = Stops(net, estate); var docks = Stops(net, port);
            if (home.Count == 0 || index >= docks.Count) return null;
            var origin = home[index % home.Count]; var destination = docks[index];
            int pick = index;
            while (portClaims.Contains(destination.Position) && ++pick < index + docks.Count)
                destination = docks[pick % docks.Count];
            if (portClaims.Contains(destination.Position)) return null;
            var outward = net.RouteToward(destination.Lane); var inward = net.RouteToward(origin.Lane);
            if (!outward.ContainsKey(origin.Lane) || !inward.ContainsKey(destination.Lane) ||
                (vehicle.CurrentEdge != destination.Lane && !outward.ContainsKey(vehicle.CurrentEdge))) return null;
            vehicle.Net = net;
            portClaims.Add(destination.Position);
            return new IndustrialFreight(vehicle, origin, destination, home, docks, portClaims, 2f + index * 9f);
        }

        public static List<Stop> Stops(LaneNet net, Rect area)
        {
            var stops = new List<Stop>();
            foreach (var road in net.Roads)
            {
                if (road.Elevated || road.Length < 50f) continue;
                var at = road.Pose(road.Length * 0.5f, 0f);
                if (!area.Contains(new Vector2(at.x, at.z))) continue;
                foreach (var lane in road.Lanes)
                    if (lane.Heading > 0 ? road.ParkingA : road.ParkingB)
                        stops.Add(new Stop(lane, road.Length * .5f));
            }
            // Distribute the first calls over all street sections before sharing a long kerb.
            int centres = stops.Count;
            for (int i = 0; i < centres; i++)
                for (float offset = 40f; offset < stops[i].S - 25f; offset += 40f)
                { stops.Add(new Stop(stops[i].Lane, stops[i].S - offset)); stops.Add(new Stop(stops[i].Lane, stops[i].S + offset)); }
            return stops;
        }

        public void Tick(float dt)
        {
            if (_disposed || dt <= 0f) return;
            if (_vehicle.Gone || _vehicle.Derelict || _vehicle.Wrecked) { Dispose(); return; }
            if (_travelling)
            {
                if ((_toPort ? _port : _estate).Arrived(_vehicle))
                { CompletedCalls++; _toPort = !_toPort; _travelling = false; _reported = false; _failures = 0; _wait = 25f; return; }
                if (_vehicle.ParkingFailed || !_vehicle.HasGoal || _progress.Stalled(dt, _vehicle.Position))
                    Retry();
                return;
            }
            _wait -= dt;
            if (_wait > 0f) return;
            var target = _toPort ? _port : _estate;
            _travelling = _vehicle.GoTo(target.Position, park: true, wantHeading: target.Lane.Heading);
            _progress.Reset(_vehicle.Position);
            if (!_travelling) Retry();
        }

        void Retry()
        {
            _travelling = false; _wait = 8f;
            if (++_failures < 3) return;
            _failures = 0;
            var choices = _toPort ? _ports : _homes;
            var old = _toPort ? _port : _estate;
            int start = choices.FindIndex(s => s.Position == old.Position);
            bool changed = false;
            var reachable = _vehicle.Net.ReachableFrom(_vehicle.CurrentEdge);
            var returnLane = _toPort ? _estate.Lane : _port.Lane;
            _vehicle.Net.RouteToward(returnLane, out var returning);
            for (int step = 1; step < choices.Count; step++)
            {
                var next = choices[(start + step) % choices.Count];
                if (_toPort && _portClaims.Contains(next.Position)) continue;
                if (!reachable.Contains(next.Lane) || !returning.ContainsKey(next.Lane)) continue;
                if (_toPort)
                { _portClaims.Remove(old.Position); _portClaims.Add(next.Position); _port = next; }
                else _estate = next;
                changed = true;
                break;
            }
            if (!_reported)
            {
                string result = changed ? "another loading stop selected" : "no alternate stop is free; will retry";
                Debug.LogWarning($"[Industry] Freight lorry {_vehicle.Id} repeatedly failed a loading call; {result}.");
                _reported = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _portClaims.Remove(_port.Position);
        }

        public static void Connect(CoreRegion region, LaneNet net)
        {
            var port = region.Quarters.Find(q => q.Slot.kind == DistrictKind.Harbor);
            if (port == null) return;
            int next = 0; var claims = new HashSet<Vector3>();
            foreach (var q in region.Quarters)
                if (q.District is IndustrialDistrict industry) industry.ConnectFreight(net, port.World, ref next, claims);
        }
    }
}
