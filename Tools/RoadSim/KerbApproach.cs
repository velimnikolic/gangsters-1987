using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using RoadDemo;
using UnityEngine;

// Exercise the public parking order with a free destination immediately beyond
// a parked car. A collision-safe wait or traffic relocation is not completion.
static class KerbApproach
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const float Deadline = 30f;

    static void Set(RoadCar car, string field, object value) =>
        typeof(RoadCar).GetField(field, Private).SetValue(car, value);

    static string Plan(RoadCar car) => string.Join(" ", new[] { "_goalS", "_parkEntryS", "_parkEntryD", "_parkEntryLen", "_sFrom", "_sLen", "_dTo" }
        .Select(name => name + "=" + typeof(RoadCar).GetField(name, Private).GetValue(car)));

    internal class ObservedCar : RoadCar
    {
        public RoadCar Obstacle;
        public bool Overlapped;
        public Action<ObservedCar> BeforeStep;
        public int Discontinuities;
        public string FirstDiscontinuity;

        internal override void TickStep(float dt)
        {
            BeforeStep?.Invoke(this);
            var beforePosition = Position;
            var beforeForward = Forward;
            float beforeSpeed = Math.Abs(Speed);
            base.TickStep(dt);
            float axle = float.IsNaN(AxleBack) || AxleBack <= 0f ? HalfLen * .6f : Math.Min(AxleBack, HalfLen);
            float axleTravel = ((Position - Forward * axle) - (beforePosition - beforeForward * axle)).magnitude;
            float yaw = Vector3.SignedAngle(beforeForward, Forward, Vector3.up);
            // Speed measures road progress. A factor of two allows the extra
            // distance along a steering curve, while still rejecting a position
            // or heading correction on a stationary vehicle, including arrival.
            float motionBudget = Math.Max(beforeSpeed, Math.Abs(Speed)) * dt * 2f;
            float yawBudget = motionBudget / 2.2f * Mathf.Rad2Deg;
            if (axleTravel > motionBudget + .025f || Math.Abs(yaw) > yawBudget + .5f)
            {
                Discontinuities++;
                FirstDiscontinuity ??= $"t={RoadCarSimulation.Now:F2} axle={axleTravel:F3}/{motionBudget:F3}m " +
                    $"yaw={yaw:F2}/{yawBudget:F2}deg {DoingLine}";
            }

            // Sweep each consecutive pair of body poses. Endpoint-only overlap
            // checks could accept an uncounted teleport through the parked body.
            if (Obstacle == null) return;
            float cornerTravel = (Position - beforePosition).magnitude +
                (HalfLen + HalfWide) * Math.Abs(yaw) * Mathf.Deg2Rad;
            int samples = Math.Max(1, (int)Math.Ceiling(cornerTravel / .05f));
            for (int sample = 0; sample <= samples; sample++)
            {
                float fraction = (float)sample / samples;
                var position = Vector3.Lerp(beforePosition, Position, fraction);
                var forward = Quaternion.Euler(0f, yaw * fraction, 0f) * beforeForward;
                Overlapped |= RoadSpace.Overlap(position, forward, HalfLen, HalfWide,
                    Obstacle.Position, Obstacle.Forward, Obstacle.HalfLen, Obstacle.HalfWide, 0f, out _);
            }
        }
    }

    public static void Run()
    {
        int cases = 0, failures = 0;
        foreach (float halfRoad in new[] { 5f, 7.5f })
            foreach (int heading in new[] { 1, -1 })
                foreach (bool parkedStart in new[] { false, true })
                    foreach (var size in new[] { (Name: "sedan", Length: 2.3f, Width: .95f),
                        (Name: "large", Length: 3.72353f, Width: 1.28412f) })
                        foreach (float dt in new[] { 1f / 30f, .2f })
                        {
                            string filter = Environment.GetEnvironmentVariable("CASE");
                            if (filter != null && filter != $"{halfRoad}/{heading}/{parkedStart}/{size.Name}/{dt:F3}") continue;
                            cases++;
                            if (!Check(halfRoad, heading, parkedStart, size.Name, size.Length, size.Width, dt)) failures++;
                        }
        foreach (int heading in new[] { 1, -1 })
            foreach (float dt in new[] { 1f / 30f, .2f })
                foreach (string scenario in new[] { "clear kerb", "behind same heading", "destination taken during pull-in" })
                {
                    string filter = Environment.GetEnvironmentVariable("CASE");
                    if (filter != null && filter != $"{scenario}/{heading}/{dt:F3}") continue;
                    cases++;
                    if (!CheckTransition(scenario, heading, dt)) failures++;
                }
        Console.WriteLine($"kerb approach: {cases - failures}/{cases} passed");
        if (failures != 0 || cases == 0) Environment.ExitCode = 1;
    }

    static bool Check(float halfRoad, int heading, bool parkedStart, string size,
        float halfLength, float halfWidth, float dt)
    {
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        Time.deltaTime = dt;
        Time.frameCount++;
        var net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, new Vector3(0f, 0f, 140f), halfRoad,
            new[] { 2.5f }, 10f, null, null, true);
        net.Finish();
        float blockerS = heading > 0 ? 65f : 75f;
        var blocker = new ObservedCar { Net = net, HalfLen = halfLength, HalfWide = halfWidth };
        bool blockerPlaced = blocker.PlaceAt(road.Pose(blockerS, road.KerbD(heading, halfWidth)), road.Axis * heading);
        Set(blocker, "<Parked>k__BackingField", true);
        Set(blocker, "<Speed>k__BackingField", 0f);
        typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(blocker, null);
        StreetTraffic.Users.Add(blocker);

        var car = new ObservedCar { Net = net, Profile = DriverProfile.Gangster,
            HalfLen = halfLength, HalfWide = halfWidth, Obstacle = blocker };
        float startS = blockerS - heading * (parkedStart ? halfLength * 2f + 3f : 25f);
        float startD = parkedStart ? road.KerbD(heading, halfWidth) : heading * 2.5f;
        bool carPlaced = car.PlaceAt(road.Pose(startS, startD), road.Axis * heading);
        bool startMatches = car.Parked == parkedStart;
        Set(car, "<Speed>k__BackingField", parkedStart ? 0f : 5f);
        StreetTraffic.Users.Add(car);
        float goalS = blockerS + heading * (halfLength * 2f + 1.5f);
        float goalD = road.KerbD(heading, halfWidth);
        var goal = road.Pose(goalS, goalD);
        bool accepted = car.GoTo(goal, park: true);
        var cars = new List<RoadCar> { car };
        car.Overlapped = RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
            blocker.Position, blocker.Forward, blocker.HalfLen, blocker.HalfWide, 0f, out _);
        float elapsed = 0f;
        var history = new List<string>();
        string lastDoing = "";
        for (int step = 0; step < Math.Ceiling(Deadline / dt) && !car.Parked; step++)
        {
            Time.time += dt;
            Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            elapsed += dt;
            if (car.DoingLine != lastDoing || step % Math.Max(1, (int)(2f / dt)) == 0)
            {
                lastDoing = car.DoingLine;
                history.Add($"t={elapsed:F1} s={car.S:F2} d={car.D:F2} v={car.Speed:F2} {lastDoing} {car.Why} {Plan(car)}");
                if (history.Count > 80) history.RemoveAt(0);
            }
        }
        float distance = (car.Position - goal).magnitude;
        // It must finish beyond the obstruction, on the requested kerb, within a
        // car length of the clicked centre. This excludes parking behind it or a
        // later arbitrary fallback on another part of the road.
        bool passed = blockerPlaced && carPlaced && startMatches && accepted && car.Parked && car.AtGoal && !car.HasGoal &&
            car.Road == road && car.Heading == heading &&
            (car.S - blockerS) * heading > halfLength * 2f && distance <= halfLength * 2f &&
            Math.Abs(car.D - goalD) <= .3f && Vector3.Dot(car.Forward, road.Axis * heading) > .999f &&
            !car.Overlapped && car.Discontinuities == 0 && car.TrafficRecoveries == 0 && elapsed <= Deadline + .01f;
        // The new arrival must not trap the neighbour's own departure.
        bool neighbourExited = false;
        if (passed)
        {
            blocker.Obstacle = car;
            blocker.GoTo(road.Pose(blockerS + heading * 45f, heading * 2.5f), false, stopAtGoal: false, wantHeading: heading);
            var departing = new List<RoadCar> { blocker };
            bool Exited() => (blocker.S - car.S) * heading > car.HalfLen + blocker.HalfLen + 1f &&
                Math.Abs(blocker.D - heading * 2.5f) < .01f && blocker.Doing == RoadCar.Manoeuvre.None;
            for (int step = 0; step < Math.Ceiling(25f / dt) && !Exited(); step++)
            {
                Time.time += dt;
                Time.frameCount++;
                RoadCarSimulation.Simulate(departing, dt);
            }
            neighbourExited = Exited() &&
                !blocker.Overlapped && blocker.Discontinuities == 0 && blocker.TrafficRecoveries == 0;
            passed &= neighbourExited;
            if (!neighbourExited) Console.WriteLine($"  neighbour departure: {blocker.Describe()} goal={blocker.HasGoal} overlap={blocker.Overlapped} {blocker.FirstDiscontinuity}");
        }
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} kerb approach {size} width={halfRoad * 2f:F0} h={heading} " +
            $"start={(parkedStart ? "parked" : "moving")} dt={dt:F3} time={elapsed:F1} distance={distance:F2} " +
            $"overlap={car.Overlapped} jumps={car.Discontinuities} recovery={car.TrafficRecoveries} " +
            $"parked={car.Parked} neighbourExited={neighbourExited} neighbourJumps={blocker.Discontinuities} {car.Describe()}");
        if (car.FirstDiscontinuity != null) Console.WriteLine("  " + car.FirstDiscontinuity);
        if (!passed || Environment.GetEnvironmentVariable("TRACE") == "1")
            foreach (string entry in history) Console.WriteLine("  " + entry);
        car.Vanish();
        blocker.Vanish();
        return passed;
    }

    static bool CheckTransition(string scenario, int heading, float dt)
    {
        StreetTraffic.Users.Clear();
        StreetTraffic.Bodies.Clear();
        StreetTraffic.Walkers.Clear();
        Time.time = 0f;
        Time.deltaTime = dt;
        Time.frameCount++;
        bool behind = scenario == "behind same heading";
        bool dynamicBlocker = scenario == "destination taken during pull-in";
        bool parkedStart = scenario == "clear kerb";
        var net = new LaneNet();
        var south = net.AddNode(0f, -7.5f, 7.5f, 7.5f, 5.7f);
        var north = net.AddNode(0f, 147.5f, 7.5f, 7.5f, 5.7f);
        var road = net.AddRoad(Vector3.zero, new Vector3(0f, 0f, 140f), 7.5f,
            new[] { 2.5f }, 10f, south, north, true);
        net.Finish();
        var car = new ObservedCar { Net = net, Profile = DriverProfile.Gangster, HalfLen = 2.3f, HalfWide = .95f };
        float goalS = 70f;
        float startS = goalS + heading * (behind ? 25f : -30f);
        float kerb = road.KerbD(heading, car.HalfWide);
        bool placed = car.PlaceAt(road.Pose(startS, parkedStart ? kerb : heading * 2.5f), road.Axis * heading);
        bool startMatches = car.Parked == parkedStart;
        Set(car, "<Speed>k__BackingField", parkedStart ? 0f : 5f);
        StreetTraffic.Users.Add(car);
        var goal = road.Pose(goalS, kerb);
        bool inserted = false;
        float insertedAt = -1f;
        if (dynamicBlocker)
            car.BeforeStep = current =>
            {
                if (inserted || current.Doing != RoadCar.Manoeuvre.PullIn) return;
                // A controlled external actor takes the destination after the
                // entry was granted. Insert only while its body is clear of ours;
                // this tests replanning rather than fixture-created penetration.
                if (RoadSpace.Overlap(current.Position, current.Forward, current.HalfLen, current.HalfWide,
                    goal, road.Axis * heading, current.HalfLen, current.HalfWide, .5f, out _)) return;
                var obstacle = new RoadCar { Net = net, HalfLen = current.HalfLen, HalfWide = current.HalfWide };
                if (!obstacle.PlaceAt(goal, road.Axis * heading)) return;
                Set(obstacle, "<Parked>k__BackingField", true);
                typeof(RoadCar).GetMethod("UpdateOccupant", Private).Invoke(obstacle, null);
                StreetTraffic.Users.Add(obstacle);
                RoadSpace.Invalidate();
                current.Obstacle = obstacle;
                inserted = true;
                insertedAt = RoadCarSimulation.Now;
            };
        bool accepted = car.GoTo(goal, park: true, wantHeading: heading);
        var cars = new List<RoadCar> { car };
        // Turning back to the same kerb includes a road journey. Ordinary and
        // dynamically obstructed forward approaches retain the 30-second limit.
        float limit = behind ? 60f : Deadline;
        float elapsed = 0f;
        bool changedHeading = false;
        var history = new List<string>();
        for (int step = 0; step < Math.Ceiling(limit / dt) && !car.Parked && !car.ParkingFailed; step++)
        {
            Time.time += dt;
            Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            elapsed += dt;
            changedHeading |= car.Heading != heading;
            if (step % Math.Max(1, (int)(2f / dt)) == 0)
            {
                history.Add($"t={elapsed:F1} s={car.S:F2} d={car.D:F2} h={car.Heading} v={car.Speed:F2} {car.DoingLine} {car.Why} {Plan(car)}");
                if (history.Count > 80) history.RemoveAt(0);
            }
        }
        float distance = (car.Position - goal).magnitude;
        float allowedDistance = dynamicBlocker ? 12f : car.HalfLen * 2f;
        bool passed = placed && startMatches && accepted && car.Parked && car.AtGoal && !car.HasGoal &&
            car.Road == road && car.Heading == heading && distance <= allowedDistance &&
            Math.Abs(car.D - kerb) <= .3f && Vector3.Dot(car.Forward, road.Axis * heading) > .999f &&
            !car.Overlapped && car.Discontinuities == 0 && car.TrafficRecoveries == 0 && elapsed <= limit + .01f &&
            (!behind || changedHeading) && (!dynamicBlocker || inserted);
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} parking transition {scenario} h={heading} dt={dt:F3} " +
            $"time={elapsed:F1}/{limit:F0} distance={distance:F2} overlap={car.Overlapped} jumps={car.Discontinuities} " +
            $"recovery={car.TrafficRecoveries} insertedAt={insertedAt:F1} failed={car.ParkingFailed} {car.Describe()}");
        if (car.FirstDiscontinuity != null) Console.WriteLine("  " + car.FirstDiscontinuity);
        if (!passed || Environment.GetEnvironmentVariable("TRACE") == "1")
            foreach (string entry in history) Console.WriteLine("  " + entry);
        car.Vanish();
        car.Obstacle?.Vanish();
        return passed;
    }
}
