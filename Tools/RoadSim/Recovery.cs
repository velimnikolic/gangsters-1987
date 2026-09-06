using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class TrafficRecoveryChecks
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Set(RoadCar car, string name, object value) => typeof(RoadCar).GetField(name, Private).SetValue(car, value);
    static object Get(RoadCar car, string name) => typeof(RoadCar).GetField(name, Private).GetValue(car);
    static bool Recover(RoadCar car, bool hidden) => (bool)typeof(RoadCar).GetMethod("TryRecoverTraffic", Private).Invoke(car, new object[] { hidden });
    static void Check(bool passed, string message)
    {
        Console.WriteLine((passed ? "PASS " : "FAIL ") + message);
        if (!passed) Environment.ExitCode = 1;
    }
    public static void Run()
    {
        var previousVisibility = RoadCar.RecoveryVisibility;
        try
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                StreetTraffic.Users.Clear(); StreetTraffic.Walkers.Clear(); StreetTraffic.Bodies.Clear();
                Time.frameCount++;
                var rotation = Quaternion.Euler(0f, repetition * 72f, 0f);
                var forward = rotation * Vector3.forward;
                var right = rotation * Vector3.right;
                var net = new LaneNet();
                var road = net.AddRoad(Vector3.zero, forward * 140f, 12.5f, new[] { 2.5f }, 10f, null, null, true);
                net.Finish();
                RoadCar Make()
                {
                    var car = new RoadCar { Net = net, Profile = DriverProfile.Police, HalfLen = 3.72f, HalfWide = 1.28f };
                    car.Spawn(road.LaneFor(1, 2.5f), 50f);
                    Set(car, "<D>k__BackingField", 9f);
                    Set(car, "_pos", road.Pose(50f, 9f));
                    Set(car, "_fwd", forward);
                    Set(car, "<Speed>k__BackingField", 0f);
                    StreetTraffic.Users.Add(car);
                    Time.frameCount++;
                    return car;
                }
                var car = Make();
                RoadCar.RecoveryVisibility = _ => false;
                car.GoTo(road.Pose(110f, 2.5f), park:false);
                var goalRoad = Get(car, "_goalRoad"); var goalStation = Get(car, "_goalS");
                var original = car.Position;
                bool recovered = Recover(car, true);
                Check(recovered && car.TrafficRecoveries == 1 && !car.Gone && car.HasGoal &&
                    ReferenceEquals(goalRoad, Get(car, "_goalRoad")) && Equals(goalStation, Get(car, "_goalS")) &&
                    car.Heading == 1 && Math.Abs(car.D - 2.5f) < .01f &&
                    (car.Position - original).magnitude > 1f && RoadSpace.Inside(car,car.Position,car.Forward,car.HalfLen,car.HalfWide,out _) == null,
                    $"hidden recovery keeps identity, destination and clear body, rotation {repetition * 72}");
                car.Vanish();

                car = Make();
                var follower = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
                follower.Spawn(road.LaneFor(1, 2.5f), 39f);
                Set(follower, "<Speed>k__BackingField", 14f);
                typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(follower, null);
                StreetTraffic.Users.Add(follower); Time.frameCount++;
                recovered = Recover(car, false);
                float followingGap = car.S - car.HalfLen - follower.S - follower.HalfLen;
                float stopping = 14f * 14f / (2f * DriverProfile.Traffic.Brake) + 14f * .3f + 3f;
                Check(recovered && (car.Heading != 1 || Math.Abs(car.D - 2.5f) > 2f ||
                    car.S + car.HalfLen < follower.S - follower.HalfLen || followingGap >= stopping),
                    "recovery cannot cut inside a moving follower's braking distance " + repetition);
                follower.Vanish(); car.Vanish();

                car = Make();
                Set(car, "<D>k__BackingField", 40f);
                Set(car, "_pos", road.Pose(50f, 40f)); original = car.Position;
                RoadCar.RecoveryVisibility = _ => true;
                Check(!Recover(car, true) && !Recover(car, false) && car.Position == original && car.TrafficRecoveries == 0,
                    "revealed traffic refuses a large relocation " + repetition);
                car.Vanish();

                car = Make();
                Set(car, "<D>k__BackingField", 4f);
                Set(car, "_pos", road.Pose(50f, 4f));
                original = car.Position;
                Check(Recover(car, false) && !car.LastTrafficRecoveryHidden &&
                    car.LastTrafficRecoveryDistance <= 2.5f &&
                    RoadSpace.Inside(car, car.Position, car.Forward, car.HalfLen, car.HalfWide, out _) == null,
                    "visible correction stays small and physically clear " + repetition);
                car.Vanish();

                car = Make(); original = car.Position;
                RoadCar.RecoveryVisibility = position => Vector3.Dot(position, right) < 5f;
                Check(!Recover(car, true) && car.Position == original,
                    "a hidden source cannot relocate into revealed destination " + repetition);
                car.Vanish();

                car = Make(); original = car.Position;
                RoadCar.RecoveryVisibility = _ => false;
                for (float station = 0f; station <= 140f; station += 2f)
                    foreach (float lateral in new[] { 2.5f, -2.5f })
                        StreetTraffic.Walkers.Add(road.Pose(station, lateral));
                Check(!Recover(car, true) && car.Position == original,
                    "recovery cannot land on a pedestrian " + repetition);
                StreetTraffic.Walkers.Clear(); car.Vanish();

                car = Make(); original = car.Position;
                var parkedCars = new List<RoadCar>();
                for (float station = 5f; station <= 135f; station += 5f)
                foreach (int heading in new[] { 1, -1 })
                {
                    var parked = new RoadCar { Net = net, Profile = DriverProfile.Traffic,
                        HalfLen = 2.3f, HalfWide = .95f };
                    parked.Spawn(road.LaneFor(heading, heading * 2.5f), heading > 0 ? station : road.Length - station);
                    Set(parked, "<Speed>k__BackingField", 0f);
                    Set(parked, "<Parked>k__BackingField", true);
                    StreetTraffic.Users.Add(parked); parkedCars.Add(parked);
                }
                Time.frameCount++;
                Check(!Recover(car, true) && car.Position == original,
                    "occupied landing places refuse recovery " + repetition);
                foreach (var parked in parkedCars) parked.Vanish();
                car.Vanish();

                foreach (string state in new[] { "parked", "halted", "wrecked" })
                {
                    car = Make(); original = car.Position;
                    if (state == "parked") Set(car, "<Parked>k__BackingField", true);
                    else if (state == "wrecked") Set(car, "<Wrecked>k__BackingField", true);
                    else car.Halt(true);
                    Check(!Recover(car, true) && car.Position == original,
                        "intentional " + state + " remains in place " + repetition);
                    car.Vanish();
                }
                car = Make(); original = car.Position;
                var watch = typeof(RoadCar).GetMethod("WatchTrafficRecovery", Private);
                watch.Invoke(car, new object[] { .1f });
                watch.Invoke(car, new object[] { 44f });
                bool waited = car.Position == original && car.TrafficRecoveries == 0;
                watch.Invoke(car, new object[] { 2f });
                Check(waited && car.TrafficRecoveries == 1, "ordinary movement gets time before automatic recovery " + repetition);
                car.Vanish();
            }
        }
        finally { RoadCar.RecoveryVisibility = previousVisibility; StreetTraffic.Walkers.Clear(); }
    }
}
