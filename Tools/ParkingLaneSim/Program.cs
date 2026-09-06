using System;
using RoadDemo;
using UnityEngine;

static class Program
{
    static int _checks;
    static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    sealed class Parked : IRoadUser
    {
        public Vector3 RoadPosition { get; set; }
        public Vector3 RoadForward { get; set; }
        public float RoadSpeed => 0f;
        public float HalfLength => 2.3f;
        public float HalfWidth => 1f;
    }

    static Carriageway Road(out LaneNet net, float half = 7.5f, bool boulevard = false, bool east = false)
    {
        net = new LaneNet();
        LaneNet.Active = net;
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        var a = net.AddNode(0f, 0f, half, half);
        var b = net.AddNode(east ? 130f : 0f, east ? 0f : 130f, half, half);
        var axis = east ? Vector3.right : Vector3.forward;
        var road = net.AddRoad(axis * half, axis * (130f - half), half,
            boulevard ? new[] { 7.5f, 12.5f } : new[] { 2.5f }, 10f, a, b, !east,
            boulevard ? 5f : 0f);
        net.Finish();
        return road;
    }

    static bool Pose(Carriageway road, float s = 40f, int side = 1, float length = 2.3f, float width = 1f)
        => ParkingLaneSlots.TryPose(road, s, side, length, width, out _, out _);

    static void Geometry()
    {
        foreach (bool east in new[] { false, true })
            foreach (bool boulevard in new[] { false, true })
                foreach (int side in new[] { -1, 1 })
                    foreach (float width in new[] { 1.035f, 1.1f, 1.15f })
                    {
                        var road = Road(out _, boulevard ? 17.5f : 7.5f, boulevard, east);
                        Check(ParkingLaneSlots.TryPose(road, 40f, side, 3.1f, width, out var at, out var facing),
                            "A measured civilian body must fit both sides of streets and boulevards.");
                        road.Project(at, out float s, out float d);
                        Check(Math.Abs(s - 40f) < .001f && Math.Sign(d) == side, "Wrong road position or side.");
                        Check(Vector3.Dot(facing, road.Axis) * side > .99f, "Parked car faces against traffic.");
                        float inner = boulevard ? 15f : 5f;
                        Check(Math.Abs(d) - width >= inner, "Visible body intrudes into the running lane.");
                        Check(Math.Abs(d) + width <= road.HalfRoad - .149f, "Visible body protrudes onto the pavement.");
                    }
        var street = Road(out _);
        Check(!Pose(street, 8f) && !Pose(street, street.Length - 8f), "Crossing mouths must remain clear.");
        Check(!Pose(street, width: 1.5f), "Oversized car must not obstruct a running lane.");
        Check(!Pose(street, side: 0) && !Pose(street, length: 0f), "Invalid bodies/side must be rejected.");
        Check(!Pose(Road(out _, 5f)), "Two-way narrow road has no parking strip.");
        street.ParkingA = false;
        Check(!Pose(street, side: -1) && Pose(street), "One-sided parking prohibition ignored.");
        street.ParkingB = false;
        Check(!Pose(street), "Road with parking disabled must stay empty.");
        street.ParkingB = true;
        street.Elevated = true;
        Check(!Pose(street), "Elevated road must stay empty.");
        street.Elevated = false;
        street.Class = RoadClass.Freeway;
        Check(!Pose(street), "Freeway must stay empty.");
        street.Class = RoadClass.Ramp;
        Check(!Pose(street), "Ramp must stay empty.");
    }

    static void OccupancyAndTraffic()
    {
        var road = Road(out var net);
        Check(ParkingLaneSlots.TryPose(road, 45f, 1, 2.3f, 1f, out var at, out var forward), "Fixture has no parking space.");
        var parked = new Parked { RoadPosition = at, RoadForward = forward };
        var occupant = net.AddStatic(parked);
        StreetTraffic.Users.Add(parked);
        Check(occupant != null && occupant.Parked, "Parked car must register outside the driving lane.");
        Check(!Pose(road, 45f) && !Pose(road, 51f), "Existing car or its pull-out gap was reused.");
        Check(Pose(road, 57f) && Pose(road, 45f, -1), "Clear distant/opposite kerb incorrectly refused.");

        var car = new DemoVehicle { Net = net, Tf = new Transform(), HalfLen = 2.3f, HalfWide = 1f };
        car.Spawn(road.LaneFor(1, 2.5f), 10f);
        StreetTraffic.Users.Add(car);
        for (int frame = 0; frame < 250; frame++)
        {
            Time.time += .05f;
            Time.frameCount++;
            car.Tick(.05f);
            Check(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLength, car.HalfWidth,
                parked.RoadPosition, parked.RoadForward, parked.HalfLength, parked.HalfWidth, 0f, out _),
                "Traffic overlapped the parked car.");
        }
        road.Project(car.Position, out float progress, out _);
        Check(progress > 65f, "Traffic failed to pass a car entirely inside the parking strip.");
        car.Despawn();
        StreetTraffic.Users.Remove(car);
        net.Remove(occupant);
        StreetTraffic.Users.Remove(parked);
        Check(road.Occupants.Count == 0 && Pose(road, 45f), "Released parking space remained occupied.");
    }

    static void Main()
    {
        Geometry();
        OccupancyAndTraffic();
        Console.WriteLine($"PASS: {_checks} assertions; parking geometry, admission, occupancy release and moving traffic.");
    }
}
