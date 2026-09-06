using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Distinguish a clear straight crossing from a real signal stop or a turn cap.
static class JunctionPace
{
    public static void Run()
    {
        foreach (int heading in new[] { 1, -1 })
            foreach (float dt in new[] { 1f / 30f, .2f })
                foreach (string signal in new[] { "none", "green", "red" })
                    Check(heading, dt, signal);
    }

    static void Check(int heading, float dt, string signal)
    {
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        Time.frameCount++;
        var net = new LaneNet();
        var node = net.AddNode(0f, 0f, 7.5f, 7.5f, 5.7f);
        if (signal != "none") node.Signal = new TrafficSignal(signal == "red" ? TrafficSignal.HalfCycle : 0f);
        var south = net.AddRoad(new Vector3(0f, 0f, -150f), new Vector3(0f, 0f, -7.5f),
            7.5f, new[] { 2.5f }, 10f, null, node, true);
        var north = net.AddRoad(new Vector3(0f, 0f, 7.5f), new Vector3(0f, 0f, 150f),
            7.5f, new[] { 2.5f }, 10f, node, null, true);
        net.Finish();
        var from = (heading > 0 ? south : north).LaneFor(heading, heading * 2.5f);
        var to = (heading > 0 ? north : south).LaneFor(heading, heading * 2.5f);
        var car = new RoadCar { Net = net, Profile = DriverProfile.Gangster };
        car.Spawn(from, from.Length - 32f);
        typeof(RoadCar).GetField("<Speed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, 14f);
        car.Route = new Dictionary<RoadEdge, RoadEdge> { { from, to } };
        StreetTraffic.Users.Add(car);
        var cars = new List<RoadCar> { car };
        float least = car.Speed;
        bool crossed = false;
        for (int i = 0; i < Math.Ceiling(6f / dt); i++)
        {
            Time.time += dt;
            Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            least = Math.Min(least, car.Speed);
            if (car.Road == to.Road && car.Progress > 8f) { crossed = true; break; }
        }
        bool ok = signal == "red" ? !crossed && car.Speed < .1f && car.Road == from.Road
            : crossed && least >= 13.9f;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} straight junction h={heading} dt={dt:F3} signal={signal} " +
            $"crossed={crossed} minimum={least:F2}m/s why={car.Why}");
        if (!ok) Environment.ExitCode = 1;
        car.Vanish();
    }
}
