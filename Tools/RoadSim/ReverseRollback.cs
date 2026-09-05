using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class ReverseRollback
{
    sealed class Body : IRoadUser
    {
        public Vector3 Position;
        public Vector3 RoadPosition => Position;
        public Vector3 RoadForward => Vector3.forward;
        public float RoadSpeed => 0;
        public float HalfLength => 2.3f;
        public float HalfWidth => 1f;
    }
    static void Set(RoadCar car, string name, object value) =>
        typeof(RoadCar).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(car, value);
    public static void Run()
    {
        foreach (float dt in new[] { 1f / 30f, .05f, .2f, .8f, 1.6f })
        {
            StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
            var net = new LaneNet();
            var a = net.AddNode(0, 0, 7.5f, 7.5f, 5.7f);
            var b = net.AddNode(0, 220, 7.5f, 7.5f, 5.7f);
            var road = net.AddRoad(new Vector3(0, 0, 7.5f), new Vector3(0, 0, 212.5f), 7.5f,
                new[] { 2.5f }, 10f, a, b, true);
            net.Finish();
            var car = new RoadCar { Net = net, Profile = DriverProfile.Police, HalfLen = 3.72f, HalfWide = 1.28f };
            car.Spawn(road.LaneFor(1, 2.5f), 100);
            var initial = car.Position;
            var body = new Body { Position = initial - Vector3.forward * 6.1f };
            StreetTraffic.Users.Add(car); StreetTraffic.Users.Add(body);
            var manoeuvre = typeof(RoadCar).GetField("_man", BindingFlags.NonPublic | BindingFlags.Instance);
            manoeuvre.SetValue(car, Enum.Parse(manoeuvre.FieldType, "Reverse"));
            Set(car, "_backLeft", 5f);
            Set(car, "<Speed>k__BackingField", 0f);
            int beforeBelts = RoadCar.BeltHits;
            float drift = 0; int overlaps = 0;
            for (int frame = 0; frame < Math.Ceiling(3f / dt); frame++)
            {
                Time.time += dt; Time.frameCount++;
                RoadCarSimulation.Simulate(new List<RoadCar> { car }, dt);
                var expected = road.Pose(car.S, car.D);
                drift = Math.Max(drift, (expected - car.Position).magnitude);
                if (RoadSpace.Overlap(car.Position, car.RoadForward, car.HalfLen, car.HalfWide,
                    body.Position, body.RoadForward, body.HalfLength, body.HalfWidth, 0f, out _)) overlaps++;
            }
            bool ok = drift < .01f && overlaps == 0 && RoadCar.BeltHits > beforeBelts;
            Console.WriteLine($"reverse rollback dt={dt:F3}: {(ok ? "PASS" : "FAIL")} drift={drift:F3} overlaps={overlaps} belts={RoadCar.BeltHits - beforeBelts} moved={(car.Position - initial).magnitude:F3}");
            if (!ok) Environment.ExitCode = 1;
            car.Vanish();
        }
    }
}
