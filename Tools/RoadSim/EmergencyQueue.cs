using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Two response cars queue behind ordinary traffic on conflicting approaches.
// Reserving both approaches while the responses cannot move strands both queues.
static class EmergencyQueue
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Call(RoadCar car, string method, params object[] args) =>
        typeof(RoadCar).GetMethod(method, Private).Invoke(car, args);
    static void Speed(RoadCar car, float speed) => typeof(RoadCar)
        .GetField("<Speed>k__BackingField", Private).SetValue(car, speed);

    public static void Run()
    {
        foreach (var dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f }) Replay(dt);
    }

    static void Replay(float dt)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        var net = new LaneNet();
        var centre = net.AddNode(0, 0, 7.5f, 7.5f, 5.7f);
        var west = net.AddNode(-110, 0, 7.5f, 7.5f);
        var east = net.AddNode(110, 0, 7.5f, 7.5f);
        var north = net.AddNode(0, 110, 7.5f, 7.5f);
        var south = net.AddNode(0, -110, 7.5f, 7.5f);
        var wr = net.AddRoad(new Vector3(-102.5f, 0, 0), new Vector3(-7.5f, 0, 0), 7.5f, new[] { 2.5f }, 10f, west, centre, false);
        var er = net.AddRoad(new Vector3(7.5f, 0, 0), new Vector3(102.5f, 0, 0), 7.5f, new[] { 2.5f }, 10f, centre, east, false);
        var nr = net.AddRoad(new Vector3(0, 0, 7.5f), new Vector3(0, 0, 102.5f), 7.5f, new[] { 2.5f }, 10f, centre, north, true);
        var sr = net.AddRoad(new Vector3(0, 0, -102.5f), new Vector3(0, 0, -7.5f), 7.5f, new[] { 2.5f }, 10f, south, centre, true);
        net.Finish();
        var cars = new List<RoadCar>();
        RoadCar Spawn(RoadEdge incoming, RoadEdge outgoing, float toEnd, bool police)
        {
            var car = new RoadCar
            {
                Net = net,
                HalfLen = police ? 3.72f : 2.9f,
                HalfWide = police ? 1.28f : 1.09f,
                Profile = police ? DriverProfile.Police : DriverProfile.Traffic
            };
            car.Spawn(incoming, incoming.Length - toEnd);
            car.Route = new Dictionary<RoadEdge, RoadEdge> { { incoming, outgoing } };
            Call(car, "PlanNext", centre); Speed(car, 0);
            cars.Add(car); StreetTraffic.Users.Add(car); return car;
        }
        var northCar = Spawn(nr.LaneFor(-1, -2.5f), sr.LaneFor(-1, -2.5f), 8.5f, false);
        var eastCar = Spawn(er.LaneFor(-1, -2.5f), wr.LaneFor(-1, -2.5f), 8.7f, false);
        var northResponse = Spawn(nr.LaneFor(-1, -2.5f), sr.LaneFor(-1, -2.5f), 25.3f, true);
        var eastResponse = Spawn(er.LaneFor(-1, -2.5f), wr.LaneFor(-1, -2.5f), 25.3f, true);
        try
        {
            bool drained = !(bool)Call(northCar, "YieldsToEmergencyAt", centre) &&
                           !(bool)Call(eastCar, "YieldsToEmergencyAt", centre);
            Speed(eastResponse, 10f);
            bool movingPriority = (bool)Call(northCar, "YieldsToEmergencyAt", centre);
            Speed(eastResponse, 0f);
            var starts = cars.Select(c => c.Position).ToArray();
            var moved = new float[cars.Count]; int overlaps = 0, jumps = 0;
            for (int frame = 0; frame < Math.Ceiling(90f / dt); frame++)
            {
                var before = cars.Select(c => c.Position).ToArray();
                Time.time = (frame + 1) * dt; Time.frameCount++;
                RoadCarSimulation.Simulate(cars, dt);
                for (int i = 0; i < cars.Count; i++)
                {
                    moved[i] = Math.Max(moved[i], (cars[i].Position - starts[i]).magnitude);
                    if ((cars[i].Position - before[i]).magnitude > 30f * dt + .15f) jumps++;
                    for (int j = i + 1; j < cars.Count; j++)
                        if (RoadSpace.Overlap(cars[i].Position, cars[i].RoadForward, cars[i].HalfLen, cars[i].HalfWide,
                            cars[j].Position, cars[j].RoadForward, cars[j].HalfLen, cars[j].HalfWide, 0f, out _)) overlaps++;
                }
            }
            bool ok = drained && movingPriority && moved.All(d => d > 30f) && overlaps == 0 && jumps == 0;
            Console.WriteLine($"== emergency queues dt={dt:F3}: {(ok ? "PASS" : "FAIL")} drain={drained} movingPriority={movingPriority} minMove={moved.Min():F1} overlaps={overlaps} jumps={jumps}");
            if (!ok) Environment.ExitCode = 1;
        }
        finally { foreach (var car in cars) car.Vanish(); }
    }
}
