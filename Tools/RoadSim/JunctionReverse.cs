using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;
static class JunctionReverse
{
    static object Call(RoadCar c, string method, params object[] args) => typeof(RoadCar).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, args);
    public static void Run()
    {
        foreach (var dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f }) Replay(dt);
    }
    static void Replay(float dt)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear(); Time.time = 0f;
        var net = new LaneNet();
        var c = net.AddNode(697.5f, 297.5f, 7.5f, 7.5f, 5.7f);
        var w = net.AddNode(667.5f, 297.5f, 7.5f, 7.5f, 5.7f);
        var e = net.AddNode(777.5f, 297.5f, 7.5f, 7.5f, 5.7f);
        var s = net.AddNode(697.5f, 222.5f, 7.5f, 7.5f, 5.7f);
        var n = net.AddNode(697.5f, 397.5f, 7.5f, 7.5f, 5.7f);
        var west = net.AddRoad(new Vector3(675, 0, 297.5f), new Vector3(690, 0, 297.5f), 7.5f, new[] { 2.5f }, 10f, w, c, false);
        var east = net.AddRoad(new Vector3(705, 0, 297.5f), new Vector3(770, 0, 297.5f), 7.5f, new[] { 2.5f }, 10f, c, e, false);
        var south = net.AddRoad(new Vector3(697.5f, 0, 230), new Vector3(697.5f, 0, 290), 7.5f, new[] { 2.5f }, 10f, s, c, true);
        net.AddRoad(new Vector3(697.5f, 0, 305), new Vector3(697.5f, 0, 390), 7.5f, new[] { 2.5f }, 10f, c, n, true);
        net.Finish();
        var queue = new RoadCar { Net = net, HalfLen = 2.342425f, HalfWide = 1.034167f, Profile = DriverProfile.Traffic };
        var turn = new RoadCar { Net = net, HalfLen = 3.080581f, HalfWide = 1.14798f, Profile = DriverProfile.Traffic };
        var straight = new RoadCar { Net = net, HalfLen = 3.080581f, HalfWide = 1.14798f, Profile = DriverProfile.Traffic };
        var cars = new List<RoadCar> { queue, turn, straight };
        var incoming = west.LaneFor(1, 2.5f); var fromSouth = south.LaneFor(1, 2.5f);
        queue.Spawn(incoming, 7.04f);
        straight.Spawn(incoming, incoming.Length); straight.Route = new Dictionary<RoadEdge, RoadEdge> { { incoming, east.LaneFor(1, 2.5f) } };
        Call(straight, "PlanNext", c); Call(straight, "EnterNode", c, 5.70f); Call(straight, "Place", 0f, float.NaN, float.NaN);
        turn.Spawn(fromSouth, fromSouth.Length); turn.Route = new Dictionary<RoadEdge, RoadEdge> { { fromSouth, west.LaneFor(-1, -2.5f) } };
        Call(turn, "PlanNext", c); Call(turn, "EnterNode", c, 1.66f); Call(turn, "Place", 0f, float.NaN, float.NaN);
        foreach (var car in cars) { StreetTraffic.Users.Add(car); typeof(RoadCar).GetField("<Speed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, 0f); }
        var a = straight.Position; var b = turn.Position; float ma = 0, mb = 0; int overlaps = 0, eased = 0, jumps = 0;
        var previous = new Vector3[cars.Count];
        for (int frame = 0; frame < Math.Ceiling(120f / dt); frame++)
        {
            for (int i = 0; i < cars.Count; i++) previous[i] = cars[i].Position;
            Time.time = (frame + 1) * dt; Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            ma = Math.Max(ma, (straight.Position - a).magnitude); mb = Math.Max(mb, (turn.Position - b).magnitude);
            for (int i = 0; i < cars.Count; i++)
            {
                if ((cars[i].Position - previous[i]).magnitude > 30f * dt + .15f) jumps++;
                for (int j = i + 1; j < cars.Count; j++)
                    if (RoadSpace.Overlap(cars[i].Position, cars[i].RoadForward, cars[i].HalfLen, cars[i].HalfWide,
                        cars[j].Position, cars[j].RoadForward, cars[j].HalfLen, cars[j].HalfWide, 0f, out _))
                    {
                        if ((cars[i].Deadlock.Ignores(cars[j]) || cars[j].Deadlock.Ignores(cars[i])) &&
                            Math.Abs(cars[i].Speed) <= RoadDeadlock.Pace + .01f &&
                            Math.Abs(cars[j].Speed) <= RoadDeadlock.Pace + .01f) eased++;
                        else overlaps++;
                    }
            }
        }
        bool ok = ma > 10 && mb > 10 && overlaps == 0 && jumps == 0;
        Console.WriteLine($"== collection junction dt={dt:F3}: {(ok ? "PASS" : "FAIL")} straight={ma:F2} turn={mb:F2} overlaps={overlaps} permittedPairSamples={eased} jumps={jumps}");
        if (!ok) Environment.ExitCode = 1;
        foreach (var car in cars) car.Vanish();
    }
}
