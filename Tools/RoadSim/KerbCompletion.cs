using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class KerbCompletion
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Set(RoadCar car, string name, object value) =>
        typeof(RoadCar).GetField(name, Private).SetValue(car, value);

    public static void Run()
    {
        foreach (float dt in new[] { .033f, .05f, .2f, .4f, .8f })
        {
            StreetTraffic.Users.Clear();
            StreetTraffic.Bodies.Clear();
            StreetTraffic.Walkers.Clear();
            Time.frameCount++;
            var net = new LaneNet();
            var road = net.AddRoad(Vector3.zero, new Vector3(0, 0, 180), 7.5f,
                new[] { 2.5f }, 10, null, null, true);
            net.Finish();
            var bike = new RoadCar { Net = net, HalfLen = .9054f, HalfWide = .34f };
            bike.Spawn(road.LaneFor(1, 2.5f), 27.83f);
            Set(bike, "<D>k__BackingField", 7.18f);
            Set(bike, "_pos", road.Pose(27.83f, 7.18f));
            Set(bike, "<Speed>k__BackingField", 0f);
            Set(bike, "<Parked>k__BackingField", true);
            StreetTraffic.Users.Add(bike);
            typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(bike, null);
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police,
                HalfLen = 3.72353f, HalfWide = 1.28412f };
            car.Spawn(road.LaneFor(1, 2.5f), 8f);
            Set(car, "<Speed>k__BackingField", 0f);
            StreetTraffic.Users.Add(car);
            bool accepted = car.GoTo(road.Pose(22f, 6.21588f), true);
            var cars = new List<RoadCar> { car };
            bool overlap = false;
            float elapsed = 0;
            for (int step = 0; step < Math.Ceiling(90f / dt) && !car.Parked; step++)
            {
                Time.time += dt;
                Time.frameCount++;
                RoadCarSimulation.Simulate(cars, dt);
                elapsed += dt;
                overlap |= RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                    bike.Position, bike.Forward, bike.HalfLen, bike.HalfWide, 0, out _);
            }
            bool passed = accepted && car.Parked && !car.HasGoal && !overlap && car.S > 15f &&
                Vector3.Dot(car.Forward, road.DirAt(car.S)) > .999f;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} parking beside motorcycle dt={dt}: " +
                $"parked={car.Parked} goal={car.HasGoal} time={elapsed:F1} overlap={overlap} {car.Describe()}");
            if (!passed) Environment.ExitCode = 1;
            car.Vanish();
            bike.Vanish();
        }
    }
}
