using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Public parking/departure orders, with every simulated substep checked for overlap.
static class KerbDeparture
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Read(RoadCar car, string field) => typeof(RoadCar).GetField(field, Private).GetValue(car);
    static void Set(RoadCar car, string field, object value) => typeof(RoadCar).GetField(field, Private).SetValue(car, value);

    sealed class ObservedCar : KerbApproach.ObservedCar
    {
        public void BreakDown() => StandDown();
        internal override void TickStep(float dt)
        {
            base.TickStep(dt);
            foreach (var other in StreetTraffic.Users)
                if (other != this && RoadSpace.Overlap(Position, Forward, HalfLen, HalfWide,
                    other.RoadPosition, other.RoadForward, other.HalfLength, other.HalfWidth, 0f, out _))
                    Overlapped = true;
        }
    }

    static Carriageway Road(float halfRoad, out LaneNet net)
    {
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        Time.frameCount++;
        net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, new Vector3(0f, 0f, 220f), halfRoad,
            new[] { 2.5f }, 10f, null, null, true);
        net.Finish();
        return road;
    }

    static ObservedCar Put(LaneNet net, Carriageway road, int heading, float s,
        float halfLength = 2.3f, float halfWidth = .95f, bool parked = true)
    {
        var car = new ObservedCar { Net = net, Profile = DriverProfile.Gangster,
            HalfLen = halfLength * RoadCar.TrafficFootprintScale,
            HalfWide = halfWidth * RoadCar.TrafficFootprintScale };
        car.PlaceAt(road.Pose(s, parked ? road.KerbD(heading, halfWidth) : heading * 2.5f), road.Axis * heading);
        Set(car, "<Speed>k__BackingField", 0f);
        StreetTraffic.Users.Add(car);
        return car;
    }

    static void Tick(List<RoadCar> cars, float dt)
    {
        Time.time += dt;
        Time.frameCount++;
        RoadCarSimulation.Simulate(cars, dt);
    }

    static void Report(bool ok, string message)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {message}");
        if (!ok) Environment.ExitCode = 1;
    }

    public static void Run()
    {
        var standard = new RoadCar();
        Report(RoadSpace.Overlap(Vector3.zero, Vector3.forward, standard.HalfLen, standard.HalfWide,
            new Vector3(0f, 0f, 4.2f), Vector3.forward, standard.HalfLen, standard.HalfWide, 0f, out _),
            "two 4.6m visible cars cannot fit at 4.2m centre spacing");
        standard.Vanish();
        foreach (int heading in new[] { 1, -1 })
            foreach (float halfRoad in new[] { 5f, 7.5f })
                foreach (bool large in new[] { false, true })
                    foreach (float dt in new[] { 1f / 30f, .2f })
                        ParkThenLeave(heading, halfRoad, large, dt);
        Reservations();
        DisabledReservation(false);
        DisabledReservation(true);
        FinalOccupancy();
        foreach (int heading in new[] { 1, -1 })
            foreach (float dt in new[] { 1f / 30f, .2f }) InterruptedExit(heading, dt);
    }

    static void InterruptedExit(int heading, float dt)
    {
        var road = Road(7.5f, out var net);
        float start = heading > 0 ? 70f : 150f;
        var car = Put(net, road, heading, start);
        ObservedCar obstacle = null;
        bool backed = false;
        car.BeforeStep = current =>
        {
            backed |= current.Doing == RoadCar.Manoeuvre.Reverse;
            if (obstacle != null || current.Doing != RoadCar.Manoeuvre.PullOut || (current.S - start) * heading < 3f) return;
            var position = road.Pose(start + heading * 10f, heading * 2.5f);
            if (RoadSpace.Overlap(current.Position, current.Forward, current.HalfLen, current.HalfWide,
                position, road.Axis * heading, current.HalfLen, current.HalfWide, .5f, out _)) return;
            obstacle = Put(net, road, heading, start + heading * 10f, parked: false);
            Set(obstacle, "<Parked>k__BackingField", true);
            typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(obstacle, null);
            car.Obstacle = obstacle;
            RoadSpace.Invalidate();
        };
        bool accepted = car.GoTo(road.Pose(start + heading * 60f, heading * 2.5f), false, stopAtGoal: false, wantHeading: heading);
        bool Exited() => (car.S - start) * heading > 20f && Math.Abs(car.D - heading * 2.5f) < .01f &&
            car.Doing == RoadCar.Manoeuvre.None;
        var cars = new List<RoadCar> { car };
        for (int i = 0; i < Math.Ceiling(35f / dt) && !Exited(); i++) Tick(cars, dt);
        Report(accepted && obstacle != null && backed && Exited() && !car.Overlapped && car.Discontinuities == 0 &&
            car.TrafficRecoveries == 0, $"interrupted exit h={heading} dt={dt:F3} backed={backed} exited={Exited()} " +
            $"overlap={car.Overlapped} jumps={car.Discontinuities} {car.Describe()}");
        car.Vanish();
        obstacle?.Vanish();
    }

    static void ParkThenLeave(int heading, float halfRoad, bool large, float dt)
    {
        var road = Road(halfRoad, out var net);
        float length = large ? 3.72f : 2.3f, width = large ? 1.28f : .95f;
        var neighbour = Put(net, road, heading, 110f, length, width);
        var car = Put(net, road, heading, 110f - heading * 50f, length, width);
        car.Obstacle = neighbour;
        float requested = 110f - heading * (2f * length + 1.6f);
        bool accepted = car.GoTo(road.Pose(requested, road.KerbD(heading, width)), true);
        float selected = (float)Read(car, "_goalS");
        var cars = new List<RoadCar> { car };
        for (int i = 0; i < Math.Ceiling(35f / dt) && !car.Parked && !car.ParkingFailed; i++)
        {
            Tick(cars, dt);
            if (Environment.GetEnvironmentVariable("TRACE") == "1" && heading == 1 && halfRoad == 5f && !large && dt > .1f && i % 5 == 0)
                Console.WriteLine($"  {Time.time:F1} s={car.S:F2} goal={Read(car, "_goalS")} v={car.Speed:F2} {car.Describe()}");
        }
        bool parked = car.Parked && car.AtGoal && !car.ParkingFailed;
        float start = car.S;
        bool Exited() => (car.S - start) * heading > 3f &&
            Math.Abs(car.D - heading * 2.5f) < .01f && car.Doing == RoadCar.Manoeuvre.None;
        if (parked)
        {
            car.GoTo(road.Pose(110f + heading * 65f, heading * 2.5f), false, stopAtGoal: false, wantHeading: heading);
            for (int i = 0; i < Math.Ceiling(25f / dt) && !Exited(); i++)
            {
                Tick(cars, dt);
                if (Environment.GetEnvironmentVariable("TRACE") == "1" && heading == 1 && halfRoad == 5f && dt > .1f && i % 5 == 0)
                    Console.WriteLine($"  exit large={large} {Time.time:F1} s={car.S:F2} d={car.D:F2} v={car.Speed:F2} target={Read(car, "_dTo")} passD={Read(car, "_manD")} past={Read(car, "_manPastS")} {car.Describe()}");
            }
        }
        bool exited = Exited();
        Report(accepted && parked && exited && Math.Abs(selected - requested) <= 15f &&
            Math.Abs(start - requested) <= 15.3f && !car.Overlapped && car.Discontinuities == 0 && car.TrafficRecoveries == 0,
            $"park then leave h={heading} width={halfRoad * 2} large={large} dt={dt:F3} " +
            $"selected={selected:F2} requested={requested:F2} parked={parked} exited={exited} overlap={car.Overlapped} jumps={car.Discontinuities} {car.Describe()}");
        car.Vanish();
        neighbour.Vanish();
    }

    static void DisabledReservation(bool wreck)
    {
        var road = Road(7.5f, out var net);
        var first = Put(net, road, 1, 20f, parked: false);
        var second = Put(net, road, 1, 35f, parked: false);
        var goal = road.Pose(100f, road.KerbD(1, second.HalfWide));
        first.GoTo(goal, true);
        if (wreck) first.Wreck(); else first.BreakDown();
        second.GoTo(goal, true);
        Report(!first.HasGoal && !second.ParkingFailed && Math.Abs((float)Read(second, "_goalS") - 100f) < .1f,
            $"{(wreck ? "wreck" : "derelict")} releases remote destination and exit reservation");
        first.Vanish();
        second.Vanish();
    }

    static void Reservations()
    {
        var road = Road(7.5f, out var net);
        var first = Put(net, road, 1, 20f, parked: false);
        var second = Put(net, road, 1, 35f, parked: false);
        var goal = road.Pose(100f, road.KerbD(1, first.HalfWide));
        first.GoTo(goal, true);
        second.GoTo(goal, true);
        float a = (float)Read(first, "_goalS"), b = (float)Read(second, "_goalS");
        Report(!first.ParkingFailed && !second.ParkingFailed && Math.Abs(a - b) > first.HalfLen + second.HalfLen,
            $"two incoming cars reserve separate destinations a={a:F2} b={b:F2}");
        second.Halt(false);
        second.GoTo(goal, true);
        Report(!second.ParkingFailed, "cancelled parking order can select a fresh slot");
        first.Vanish();
        second.Halt(false);
        second.GoTo(goal, true);
        Report(Math.Abs((float)Read(second, "_goalS") - 100f) < .1f,
            "despawn releases destination and departure space");
        second.Vanish();
    }

    // A body registered only with the physical index must veto completion too.
    sealed class LateBody : IRoadUser
    {
        public Vector3 RoadPosition { get; set; }
        public Vector3 RoadForward => Vector3.forward;
        public float RoadSpeed => 0f;
        public float HalfLength => 2.3f;
        public float HalfWidth => .95f;
    }

    static void FinalOccupancy()
    {
        var road = Road(7.5f, out var net);
        var car = Put(net, road, 1, 40f);
        car.GoTo(road.Pose(100f, road.KerbD(1, car.HalfWide)), true);
        var body = new LateBody { RoadPosition = road.Pose((float)Read(car, "_goalS"),
            road.KerbD(1, car.HalfWide)) };
        StreetTraffic.Users.Add(body);
        RoadSpace.Invalidate();
        bool completed = (bool)typeof(RoadCar).GetMethod("ParkingCanComplete", Private).Invoke(car, null);
        Report(!completed && !car.AtGoal, "late physical occupant vetoes parking completion");
        StreetTraffic.Users.Remove(body);
        car.Vanish();
    }
}
