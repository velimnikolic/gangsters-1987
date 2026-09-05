using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Replays the three leading bodies from the MiniCore court-run gridlock.
// The van's belt-limited physical pose matters: a connector-aligned spawn
// misses the failure entirely. Setup is isolated model state, not a live command.
static class JunctionYield
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static object Call(RoadCar car, string method, params object[] args) =>
        typeof(RoadCar).GetMethod(method, Private).Invoke(car, args);

    static void Set(RoadCar car, string field, object value) =>
        typeof(RoadCar).GetField(field, Private).SetValue(car, value);

    public static void Run()
    {
        foreach (float dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f })
            foreach (bool walkerBehind in new[] { false, true })
                Replay(dt, walkerBehind);
    }

    static void Replay(float dt, bool walkerBehind)
    {
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        var net = new LaneNet();
        var centre = net.AddNode(462.5f, 222.5f, 7.5f, 7.5f, 5.7f);
        var east = net.AddNode(570f, 222.5f, 7.5f, 7.5f, 5.7f);
        var north = net.AddNode(462.5f, 330f, 7.5f, 7.5f, 5.7f);
        var west = net.AddNode(350f, 222.5f, 7.5f, 7.5f, 5.7f);
        var south = net.AddNode(462.5f, 120f, 7.5f, 7.5f, 5.7f);
        var eastRoad = net.AddRoad(new Vector3(470f, 0f, 222.5f),
            new Vector3(east.XMin, 0f, 222.5f), 7.5f, new[] { 2.5f }, 10f, centre, east, false);
        var northRoad = net.AddRoad(new Vector3(462.5f, 0f, 230f),
            new Vector3(462.5f, 0f, north.ZMin), 7.5f, new[] { 2.5f }, 10f, centre, north, true);
        net.AddRoad(new Vector3(west.XMax, 0f, 222.5f),
            new Vector3(455f, 0f, 222.5f), 7.5f, new[] { 2.5f }, 10f, west, centre, false);
        net.AddRoad(new Vector3(462.5f, 0f, south.ZMax),
            new Vector3(462.5f, 0f, 215f), 7.5f, new[] { 2.5f }, 10f, south, centre, true);
        net.Finish();
        var incoming = eastRoad.LaneFor(-1, -2.5f);
        var outgoing = northRoad.LaneFor(1, 2.5f);
        var van = new RoadCar { Net = net, HalfLen = 4.038964f, HalfWide = 1.284147f, Profile = DriverProfile.Traffic };
        var patrol = new RoadCar { Net = net, HalfLen = 3.723526f, HalfWide = 1.284119f, Profile = DriverProfile.Police };
        var queue = new RoadCar { Net = net, HalfLen = 2.342425f, HalfWide = 1.034167f, Profile = DriverProfile.Traffic };
        var cars = new List<RoadCar> { van, patrol, queue };
        try
        {
            van.Spawn(incoming, incoming.Length);
            van.Route = new Dictionary<RoadEdge, RoadEdge> { [incoming] = outgoing };
            Call(van, "PlanNext", centre);
            Call(van, "EnterNode", centre, 7.5f);
            Call(van, "Place", 0f, float.NaN, float.NaN);
            var patrolLane = northRoad.LaneFor(-1, -2.5f);
            patrol.Spawn(patrolLane, patrolLane.Length - 5.61f);
            patrol.GoTo(new Vector3(380f, 0f, 225f), false);
            queue.Spawn(incoming, incoming.Length - 2.3f);
            queue.GoTo(new Vector3(380f, 0f, 225f), false);
            Call(queue, "PlanNext", centre);
            Call(queue, "EnterBox", centre);
            foreach (var car in cars)
            {
                StreetTraffic.Users.Add(car);
                Set(car, "<Speed>k__BackingField", 0f);
            }
            van.Slid(new Vector3(464.53f, 0f, 229.06f), new Vector3(-.53f, 0f, .85f));
            Set(van, "_beltAt", 0f);
            Set(van, "_beltFor", 2.5f);
            Set(van, "_wedgedFor", 2.47f);
            Set(van, "_boxStuck", .985f);
            var start = van.Position;
            var patrolStart = patrol.Position;
            if (walkerBehind)
                StreetTraffic.Walkers.Add(patrol.Position - patrol.RoadForward * (patrol.HalfLen + .5f));
            int overlaps = 0;
            float progress = 0f, guardedMovement = 0f;
            for (int frame = 0; frame < Math.Ceiling(120f / dt); frame++)
            {
                if (walkerBehind && frame * dt >= 3f) StreetTraffic.Walkers.Clear();
                Time.time = (frame + 1) * dt;
                Time.frameCount++;
                RoadCarSimulation.Simulate(cars, dt);
                progress = Math.Max(progress, (van.Position - start).magnitude);
                if (walkerBehind && Time.time <= 3f)
                    guardedMovement = Math.Max(guardedMovement, (patrol.Position - patrolStart).magnitude);
                for (int a = 0; a < cars.Count; a++)
                    for (int b = a + 1; b < cars.Count; b++)
                        if (RoadSpace.Overlap(cars[a].Position, cars[a].RoadForward, cars[a].HalfLen, cars[a].HalfWide,
                            cars[b].Position, cars[b].RoadForward, cars[b].HalfLen, cars[b].HalfWide, 0f, out _)) overlaps++;
            }
            bool passed = overlaps == 0 && progress > 10f && guardedMovement <= .01f;
            Console.WriteLine($"== junction yield dt={dt:F3} walker={walkerBehind}: {(passed ? "PASS" : "FAIL")} overlaps={overlaps} progress={progress:F2} guardedMovement={guardedMovement:F3}");
            if (!passed) Environment.ExitCode = 1;
        }
        finally
        {
            foreach (var car in cars) car.Vanish();
            StreetTraffic.Walkers.Clear();
        }
    }
}
