using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Replay the west boulevard edge: its left turn overlaps the neighbouring
// dead-end turn, though the two connectors belong to different RoadNodes.
static class AdjacentJunction
{
    static object Call(RoadCar car, string name, params object[] args) =>
        typeof(RoadCar).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(car, args);

    public static void Run()
    {
        foreach (var dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f }) Replay(dt);
    }

    static void Replay(float dt)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        var net = new LaneNet();
        var lower = net.AddNode(7.5f, 342.5f, 7.5f, 7.5f);
        var upper = net.AddNode(7.5f, 362.5f, 7.5f, 7.5f);
        var east = net.AddNode(97.5f, 352.5f, 7.5f, 17.5f);
        var south = net.AddNode(7.5f, 292.5f, 7.5f, 7.5f);
        var north = net.AddNode(7.5f, 472.5f, 7.5f, 7.5f);
        var blvd = net.AddRoad(new Vector3(15, 0, 352.5f), new Vector3(90, 0, 352.5f), 17.5f, new[] { 7.5f, 12.5f }, 13f, lower, east, false, 5f);
        var lowRoad = net.AddRoad(new Vector3(7.5f, 0, 300), new Vector3(7.5f, 0, 335), 7.5f, new[] { 2.5f }, 9f, south, lower, true);
        var highRoad = net.AddRoad(new Vector3(7.5f, 0, 370), new Vector3(7.5f, 0, 465), 7.5f, new[] { 2.5f }, 9f, upper, north, true);
        net.Finish();
        var fromEast = blvd.LaneFor(-1, -12.5f); var fromNorth = highRoad.LaneFor(-1, -2.5f);
        var turn = new RoadCar { Net = net, HalfLen = 3.7780807f, HalfWide = 1.284147f, Profile = DriverProfile.Traffic };
        var patrol = new RoadCar { Net = net, HalfLen = 3.723523f, HalfWide = 1.2841463f, Profile = DriverProfile.Traffic };
        var queueEast = new RoadCar { Net = net, HalfLen = 3.080581f, HalfWide = 1.1479797f, Profile = DriverProfile.Traffic };
        var queueNorth = new RoadCar { Net = net, HalfLen = 3.731269f, HalfWide = 1.284147f, Profile = DriverProfile.Traffic };
        var cars = new List<RoadCar> { queueEast, queueNorth, turn, patrol };
        queueEast.Spawn(fromEast, fromEast.Length - 4.38f);
        queueNorth.Spawn(fromNorth, fromNorth.Length - 7.38f);
        turn.Spawn(fromEast, fromEast.Length);
        turn.Route = new Dictionary<RoadEdge, RoadEdge> { { fromEast, lowRoad.LaneFor(-1, -2.5f) } };
        Call(turn, "PlanNext", lower); Call(turn, "EnterNode", lower, 4.8f); Call(turn, "Place", 0f, float.NaN, float.NaN);
        patrol.Spawn(fromNorth, fromNorth.Length);
        patrol.Route = new Dictionary<RoadEdge, RoadEdge> { { fromNorth, highRoad.LaneFor(1, 2.5f) } };
        Call(patrol, "PlanNext", upper); Call(patrol, "EnterNode", upper, 2.3f); Call(patrol, "Place", 0f, float.NaN, float.NaN);
        foreach (var car in cars)
        {
            StreetTraffic.Users.Add(car);
            typeof(RoadCar).GetField("<Speed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, 0f);
        }
        var startTurn = turn.Position; var startPatrol = patrol.Position;
        float movedTurn = 0, movedPatrol = 0; int overlaps = 0, jumps = 0, worsened = 0;
        var initialDepth = new float[cars.Count, cars.Count];
        for (int i = 0; i < cars.Count; i++) for (int j = i + 1; j < cars.Count; j++)
            if (RoadSpace.Overlap(cars[i].Position, cars[i].RoadForward, cars[i].HalfLen, cars[i].HalfWide,
                cars[j].Position, cars[j].RoadForward, cars[j].HalfLen, cars[j].HalfWide, 0f, out var push))
                initialDepth[i, j] = push.magnitude;
        var previous = new Vector3[cars.Count];
        for (int frame = 0; frame < Math.Ceiling(120f / dt); frame++)
        {
            for (int i = 0; i < cars.Count; i++) previous[i] = cars[i].Position;
            Time.time = (frame + 1) * dt; Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            movedTurn = Math.Max(movedTurn, (turn.Position - startTurn).magnitude);
            movedPatrol = Math.Max(movedPatrol, (patrol.Position - startPatrol).magnitude);
            for (int i = 0; i < cars.Count; i++)
            {
                if ((cars[i].Position - previous[i]).magnitude > 30f * dt + .15f) jumps++;
                for (int j = i + 1; j < cars.Count; j++)
                    if (RoadSpace.Overlap(cars[i].Position, cars[i].RoadForward, cars[i].HalfLen, cars[i].HalfWide,
                        cars[j].Position, cars[j].RoadForward, cars[j].HalfLen, cars[j].HalfWide, 0f, out var push))
                    {
                        if (Time.time > 5f) overlaps++;
                        if (push.magnitude > initialDepth[i, j] + .001f) worsened++;
                    }
            }
        }
        bool passed = movedTurn > 10f && movedPatrol > 10f && overlaps == 0 && jumps == 0 && worsened == 0;
        Console.WriteLine($"== adjacent junction dt={dt:F3}: {(passed ? "PASS" : "FAIL")} turn={movedTurn:F2} patrol={movedPatrol:F2} overlapsAfter5s={overlaps} worsened={worsened} jumps={jumps}");
        if (!passed) Environment.ExitCode = 1;
        foreach (var car in cars) car.Vanish();
    }
}
