using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;
static class StraightExitClearance
{

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Call(RoadCar car, string method, params object[] args) =>
        typeof(RoadCar).GetMethod(method, Private).Invoke(car, args);
    public static void Run()
    {
        foreach (float dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f })
        {
            StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
            Time.time = 0f; Time.frameCount++;
            var net = new LaneNet();
            var south = net.AddNode(97.5f, 472.5f, 7.5f, 7.5f, 5.7f);
            var centre = net.AddNode(97.5f, 522.5f, 7.5f, 7.5f, 5.7f);
            var north = net.AddNode(97.5f, 572.5f, 7.5f, 7.5f, 5.7f);
            var approach = net.AddRoad(new Vector3(97.5f, 0, 480), new Vector3(97.5f, 0, 515),
                7.5f, new[] { 2.5f }, 10f, south, centre, true);
            var exit = net.AddRoad(new Vector3(97.5f, 0, 530), new Vector3(97.5f, 0, 565),
                7.5f, new[] { 2.5f }, 10f, centre, north, true);
            net.Finish();
            var incoming = approach.LaneFor(1, 2.5f); var outgoing = exit.LaneFor(1, 2.5f);
            var transfer = new RoadCar { Net = net, HalfLen = 3.723523f, HalfWide = 1.28414917f, Profile = DriverProfile.Police };
            var parked = new RoadCar { Net = net, HalfLen = 3.723523f, HalfWide = 1.28417969f, Profile = DriverProfile.Police };
            transfer.Spawn(incoming, incoming.Length);
            transfer.Route = new Dictionary<RoadEdge, RoadEdge> { { incoming, outgoing } };
            Call(transfer, "PlanNext", centre); Call(transfer, "EnterNode", centre, 14.98f);
            Call(transfer, "Place", 0f, float.NaN, float.NaN);
            typeof(RoadCar).GetField("<Speed>k__BackingField", Private).SetValue(transfer, 0f);
            parked.Spawn(outgoing, 10.91f);
            parked.Slid(new Vector3(104.1f, 0, 540.91f));
            typeof(RoadCar).GetProperty("Parked").SetValue(parked, true);
            typeof(RoadCar).GetField("<Speed>k__BackingField", Private).SetValue(parked, 0f);
            Call(parked, "UpdateOccupant");
            StreetTraffic.Users.Add(transfer); StreetTraffic.Users.Add(parked);
            var start = transfer.Position; int overlaps = 0; float clearedAt = float.PositiveInfinity;
            var cars = new List<RoadCar> { transfer };
            for (int frame = 0; frame < Math.Ceiling(20f / dt); frame++)
            {
                Time.time += dt; Time.frameCount++;
                RoadCarSimulation.Simulate(cars, dt);
                if (float.IsPositiveInfinity(clearedAt) && (transfer.Position - start).magnitude > 3f)
                    clearedAt = Time.time;
                if (RoadSpace.Overlap(transfer.Position, transfer.Forward, transfer.HalfLen, transfer.HalfWide,
                    parked.Position, parked.Forward, parked.HalfLen, parked.HalfWide, 0f, out _)) overlaps++;
            }
            float moved = (transfer.Position - start).magnitude;
            bool ok = moved > 15f && overlaps == 0 && clearedAt <= 3.2f;
            Console.WriteLine($"straight exit dt={dt:F3}: {(ok ? "PASS" : "FAIL")} moved={moved:F3} overlaps={overlaps} clearedAt={clearedAt:F2}");
            if (!ok) Environment.ExitCode = 1;
            transfer.Vanish(); parked.Vanish();
        }
    }
}

