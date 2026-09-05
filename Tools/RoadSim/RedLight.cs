using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoadDemo;

/// <summary>
/// The getaway across a quarter full of signals - what a drive-by actually is, and the
/// thing the player asked about twice: "why do they stop at a light in the middle of a
/// drive-by?"
///
/// The machine used to (Getaway carried RunsRed = false). The first attempt at giving it
/// the red jammed, because RunsRed was ALSO the flag for "push past a queue" and a
/// machine with no crown and no far lane has nowhere to push TO. Those are two questions
/// now (DriverProfile.PushesPastQueues), which settles that half.
///
/// What this measures is whether the red itself is worth having. Three numbers per trip:
/// how long the machine is held at junctions, how long the whole crossing takes, and
/// what the belt refuses. The CONTROL run matters as much as the rest - belt refusals
/// are a running total, so a change that lengthens the trip collects more of the
/// TRAFFIC'S own refusals and reads as though the machine caused them.
/// </summary>
static class RedLight
{
    static float Dt => Program.Dt;

    // a 4x4 of signalled crossroads: any run across it meets several lights
    static LaneNet Quarter()
    {
        var xs = new float[] { -200, -60, 80, 220 };
        var zs = new float[] { -180, -40, 100, 240 };
        var net = new LaneNet();
        var nodes = new RoadNode[xs.Length, zs.Length];
        for (int i = 0; i < xs.Length; i++)
            for (int j = 0; j < zs.Length; j++)
            {
                nodes[i, j] = net.AddNode(xs[i], zs[j], 5f, 5f, 5.7f);
                nodes[i, j].I = i; nodes[i, j].J = j;
                nodes[i, j].Signal = new TrafficSignal(((i * 31 + j * 17) % 13) / 13f * TrafficSignal.Cycle);
            }
        for (int i = 0; i < xs.Length; i++)
            for (int j = 0; j + 1 < zs.Length; j++)
                net.AddRoad(new Vector3(xs[i], 0, nodes[i, j].ZMax), new Vector3(xs[i], 0, nodes[i, j + 1].ZMin),
                            5f, new[] { 2.5f }, 10f, nodes[i, j], nodes[i, j + 1], true);
        for (int j = 0; j < zs.Length; j++)
            for (int i = 0; i + 1 < xs.Length; i++)
                net.AddRoad(new Vector3(nodes[i, j].XMax, 0, zs[j]), new Vector3(nodes[i + 1, j].XMin, 0, zs[j]),
                            5f, new[] { 2.5f }, 10f, nodes[i, j], nodes[i + 1, j], false);
        net.Finish();
        return net;
    }

    static List<RoadCar> Fill(LaneNet net, int traffic)
    {
        var cars = new List<RoadCar>();
        int placed = 0;
        for (int round = 0; placed < traffic && round < 40; round++)
            foreach (var e in net.Edges)
            {
                if (placed >= traffic) break;
                float s = 6f + round * 18f;
                if (s > e.Length - 12f) continue;
                var t = new RoadCar { HalfLen = 2.3f, HalfWide = 0.95f, Net = net, Profile = DriverProfile.Traffic };
                t.Spawn(e, s);
                StreetTraffic.Users.Add(t); cars.Add(t); placed++;
            }
        return cars;
    }

    public static void Run()
    {
        DriveTrace.On = Environment.GetEnvironmentVariable("TRACE") == "1";
        Sweep("empty quarter", 0);
        Sweep("busy quarter ", 60);
        Control(60, 400f);
    }

    // Several departures: one crossing can be lucky with the phases and meet no red at
    // all, which is how the first version of this measured nothing and reported the fix
    // and the fault as identical.
    static void Sweep(string title, int traffic)
    {
        float trip = 0f, red = 0f, belt = 0f, worst = 0f, secs = 0f;
        int runs = 8, arrived = 0;
        for (int k = 0; k < runs; k++)
            Go(traffic, k * 3.1f, ref trip, ref red, ref belt, ref worst, ref arrived, ref secs);
        Console.WriteLine($"== red-light {title} ({traffic} cars, {runs} departures): " +
                          $"arrived {arrived}/{runs}, mean trip {trip / runs,5:F1}s, " +
                          $"held at junctions {red / runs,4:F1}s a trip (worst single wait {worst:F1}s), " +
                          $"belt {belt:F0} over {secs:F0}s = {belt / Math.Max(1f, secs):F1}/s");
    }

    static void Go(int traffic, float depart, ref float tripSum, ref float redSum,
                   ref float beltSum, ref float worst, ref int arrived, ref float secs)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        var net = Quarter();
        var cars = Fill(net, traffic);

        // the machine: bottom-left of the quarter, sent to the far top-right kerb
        var start = net.Roads.First(r => Math.Abs(r.A.z + 180f) < 0.1f && Math.Abs(r.A.x + 195f) < 6f);
        var bike = new RoadCar { HalfLen = 1.05f, HalfWide = 0.42f, Net = net, Profile = DriverProfile.Getaway };
        bike.Spawn(start.Lanes.First(l => l.Heading > 0), 10f);
        StreetTraffic.Users.Add(bike); cars.Add(bike);

        var far = net.Roads.First(r => Math.Abs(r.A.z - 240f) < 0.1f && Math.Abs(r.A.x - 85f) < 8f);
        var goal = far.Pose(far.Length * 0.5f, far.KerbD(1, 0.42f));

        Time.deltaTime = Dt;
        RoadCar.BeltHits = 0;

        float arrivedAt = -1f, atRed = 0f, thisRed = 0f, ran = 0f;
        bool sent = false;
        bool reportedBelt = false;
        for (int f = 0; f < (int)(400f / Dt); f++)
        {
            Time.time = f * Dt; Time.frameCount = f;
            // the signals run on before he sets off, so the eight trips meet different phases
            if (!sent && Time.time >= depart) { sent = true; bike.GoTo(goal, park: true); }
            RoadCarSimulation.Simulate(cars, Dt);
            if (!reportedBelt && RoadCar.BeltHits > 0 && Environment.GetEnvironmentVariable("TRACE") == "1")
            {
                Console.WriteLine($"   red-light departure {depart:F1}, t={Time.time:F1}: {RoadCar.LastBeltHit}");
                reportedBelt = true;
            }
            ran = Time.time;
            if (!sent) continue;
            // HELD BY A LIGHT, as against held by a car. Why is the driver's own word for
            // it and the sim already tests it for exactly these constants.
            if (bike.Why == "red" || bike.Why == "yellow" || bike.Why == "red: traffic")
            { atRed += Dt; thisRed += Dt; worst = Math.Max(worst, thisRed); }
            else thisRed = 0f;
            if (arrivedAt < 0f && bike.Parked) { arrivedAt = Time.time - depart; break; }
        }
        if (arrivedAt >= 0f) arrived++;
        tripSum += arrivedAt >= 0f ? arrivedAt : 400f;
        redSum += atRed;
        beltSum += RoadCar.BeltHits;
        secs += ran;
        if (arrivedAt < 0f || RoadCar.BeltHits > 0) Environment.ExitCode = 1;
    }

    // The same quarter for the same time with NO machine in it: how many of those belt
    // refusals were never the machine's to begin with.
    static void Control(int traffic, float seconds)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        var net = Quarter();
        var cars = Fill(net, traffic);
        Time.deltaTime = Dt; RoadCar.BeltHits = 0;
        for (int f = 0; f < (int)(seconds / Dt); f++)
        {
            Time.time = f * Dt; Time.frameCount = f;
            RoadCarSimulation.Simulate(cars, Dt);
        }
        Console.WriteLine($"== red-light CONTROL: {traffic} cars, NO machine, {seconds:F0}s: " +
                          $"belt {RoadCar.BeltHits} = {RoadCar.BeltHits / seconds:F1}/s (the traffic's own)");
        if (RoadCar.BeltHits > 0) Environment.ExitCode = 1;
    }
}
