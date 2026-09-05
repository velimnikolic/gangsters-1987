using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class BlockedYield
{
    const float Step = .033f;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Set(RoadCar car, string name, object value) =>
        typeof(RoadCar).GetField(name, Private).SetValue(car, value);

    public static void Run()
    {
        foreach (float angle in new[] { 0f, 72f, 144f, 216f, 288f })
        {
            StreetTraffic.Users.Clear();
            StreetTraffic.Bodies.Clear();
            StreetTraffic.Walkers.Clear();
            Time.frameCount++;
            var rotation = Quaternion.Euler(0, angle, 0);
            Vector3 At(float x, float z) => rotation * new Vector3(x, 0, z);
            var net = new LaneNet();
            var centre = At(0, 42.5f);
            var node = net.AddNode(centre.x, centre.z, 7.5f, 7.5f, 5.7f);
            var road = net.AddRoad(At(0, 0), At(0, 35), 7.5f,
                new[] { 2.5f }, 10, null, node, true);
            net.AddRoad(At(0, 50), At(0, 150), 7.5f,
                new[] { 2.5f }, 10, node, null, true);
            net.Finish();
            var other = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.723523f, HalfWide = 1.28417969f };
            other.Spawn(road.LaneFor(1, 2.5f), 21.63818f);
            Set(other, "<D>k__BackingField", 5.06420374f);
            Set(other, "_pos", At(5.4221525f, 21.4914f));
            Set(other, "_fwd", At(-.356446236f, .93431586f));
            Set(other, "<Speed>k__BackingField", 0f);
            StreetTraffic.Users.Add(other);
            typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(other, null);
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.723584f, HalfWide = 1.28411865f };
            car.Spawn(road.LaneFor(1, 2.5f), 28.6505985f);
            Set(car, "<Speed>k__BackingField", 0f);
            Set(car, "_beltFor", 1f);
            StreetTraffic.Users.Add(car);
            var start = car.Position;
            var cars = new List<RoadCar> { car };
            bool overlap = false;
            float moved = 0;
            for (int step = 0; step < 900 && moved <= 20f; step++)
            {
                Time.time += Step;
                Time.frameCount++;
                RoadCarSimulation.Simulate(cars, Step);
                moved = Math.Max(moved, (car.Position - start).magnitude);
                overlap |= RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                    other.Position, other.Forward, other.HalfLen, other.HalfWide, 0, out _);
            }
            bool passed = moved > 20f && !overlap;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} blocked reverse angle={angle}: " +
                $"moved={moved:F3} overlap={overlap} {car.Describe()}");
            if (!passed) Environment.ExitCode = 1;
            car.Vanish();
            other.Vanish();
        }
    }
}
