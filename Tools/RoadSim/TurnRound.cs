using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoadDemo;

/// <summary>
/// The turn in the road, which the player reported twice over: a machine sent at a mark
/// behind it "does not turn round in the street it is in - it either rides to the end of
/// the street to turn, or goes the whole way round the block."
///
/// Both halves were real and they were different faults.
///
/// TURNWHEN measures the first: how long a driver takes to turn round on the street he
/// is standing on, with the goal behind him. The throttle was held to UTurnSpeed + 2
/// while he meant to turn, and the turn's own gate admits arcSpeed + 1.5 where arcSpeed
/// is UTurnSpeed at best - so the two never met and the turn was only ever granted
/// where something ELSE had slowed the car. Before: 23-25 s, 260-300 m of driving, and
/// it parked at the far end of the street instead of at the mark. After: 3 s, 31 m, on
/// the spot it was sent to.
///
/// TURNFIRST measures the second: a mark on ANOTHER street, behind. The lane graph has
/// no U-turn edge in the middle of a street, so the route table could only ever draw
/// the way forward - round the block. Before: 36.6 s, 457 m for a mark 100 m away
/// (4.6x), and it never reached it. After: 10.7 s, 114 m (1.1x), parked at the mark.
///
/// Keep both at their "after" figures before touching RoadCar's turn-round.
/// </summary>
static class TurnRound
{
    static float Dt => Program.Dt;

    // ------------------------------------------------------------ one plain street

    static LaneNet Street(out Carriageway road)
    {
        var net = new LaneNet();
        var a = net.AddNode(-120f, 0f, 5f, 5f, 5.7f);
        var b = net.AddNode(120f, 0f, 5f, 5f, 5.7f);
        net.AddRoad(new Vector3(a.XMax, 0, 0f), new Vector3(b.XMin, 0, 0f), 5f, new[] { 2.5f }, 10f, a, b, false);
        net.Finish();
        road = net.Roads[0];
        return net;
    }

    static void TurnWhenOne(string title, int oncoming, DriverProfile profile)
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        var net = Street(out var road);
        var cars = new List<RoadCar>();
        var east = road.Lanes.First(l => l.Heading > 0);
        var west = road.Lanes.First(l => l.Heading < 0);

        var g = new RoadCar { HalfLen = 1.05f, HalfWide = 0.42f, Net = net, Profile = profile };
        g.Spawn(east, 30f);
        StreetTraffic.Users.Add(g); cars.Add(g);
        for (int i = 0; i < oncoming; i++)
        {
            var t = new RoadCar { HalfLen = 2.3f, HalfWide = 0.95f, Net = net, Profile = DriverProfile.Traffic };
            t.Spawn(west, 20f + i * 26f);
            StreetTraffic.Users.Add(t); cars.Add(t);
        }

        Time.deltaTime = Dt;
        RoadCar.BeltHits = 0;
        g.GoTo(road.Pose(12f, road.KerbD(-1, 0.42f)), park: true);   // the kerb behind him

        float turnedAt = -1f, parkedAt = -1f, driven = 0f, topSpeed = 0f;
        var was = g.Position;
        int h0 = g.Heading;
        for (int f = 0; f < (int)(120f / Dt); f++)
        {
            Time.time = f * Dt; Time.frameCount = f;
            RoadCarSimulation.Simulate(cars, Dt);
            driven += Vector3.Distance(g.Position, was); was = g.Position;
            topSpeed = Math.Max(topSpeed, Math.Abs(g.Speed));
            if (turnedAt < 0f && g.Heading != h0) turnedAt = Time.time;
            if (parkedAt < 0f && g.Parked) { parkedAt = Time.time; break; }
        }
        Console.WriteLine($"== turn-round {title}: turned {turnedAt,5:F1}s, parked {parkedAt,5:F1}s, " +
                          $"drove {driven,4:F0} m, top {topSpeed:F1} m/s, belt={RoadCar.BeltHits}, " +
                          $"ended s={g.S:F1} d={g.D:F1} h={g.Heading} (want s~12, h=-1)");
    }

    // ------------------------------------------------------------ a mark one street back

    static void TurnFirstOne()
    {
        // two east-west streets in a line, cut by a crossroads, with north-south streets
        // through it: the plainest shape the fault appears on
        var net = new LaneNet();
        float[] xs = { -200f, -60f, 80f, 220f };
        float[] zs = { -180f, -40f, 100f };
        var nodes = new RoadNode[xs.Length, zs.Length];
        for (int i = 0; i < xs.Length; i++)
            for (int j = 0; j < zs.Length; j++)
            {
                nodes[i, j] = net.AddNode(xs[i], zs[j], 5f, 5f, 5.7f);
                nodes[i, j].I = i; nodes[i, j].J = j;
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

        var here = net.Roads.First(r => Math.Abs(r.A.z + 40f) < 0.1f && Math.Abs(r.A.x + 55f) < 6f);
        var back = net.Roads.First(r => Math.Abs(r.A.z + 40f) < 0.1f && Math.Abs(r.A.x + 195f) < 6f);

        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        var bike = new RoadCar { HalfLen = 1.05f, HalfWide = 0.42f, Net = net, Profile = DriverProfile.Getaway };
        bike.Spawn(here.Lanes.First(l => l.Heading > 0), 25f);
        StreetTraffic.Users.Add(bike);

        var goal = back.Pose(back.Length * 0.5f, back.KerbD(-1, 0.42f));
        float crow = Vector3.Distance(bike.Position, goal);

        Time.deltaTime = Dt;
        RoadCar.BeltHits = 0;
        bike.GoTo(goal, park: true);

        float driven = 0f, arrivedAt = -1f, east = bike.Position.x;
        var was = bike.Position;
        var moving = new[] { bike };
        for (int f = 0; f < (int)(240f / Dt); f++)
        {
            Time.time = f * Dt; Time.frameCount = f;
            RoadCarSimulation.Simulate(moving, Dt);
            driven += Vector3.Distance(bike.Position, was); was = bike.Position;
            east = Math.Max(east, bike.Position.x);
            if (arrivedAt < 0f && bike.Parked) { arrivedAt = Time.time; break; }
        }
        // how near the mark it actually finished - the number that caught the old code
        // parking on a street two turns away and calling it done
        float missedBy = Vector3.Distance(bike.Position, goal);
        Console.WriteLine($"== turn-first (mark {crow:F0} m back, on the next street): " +
                          $"parked {arrivedAt,5:F1}s, drove {driven,4:F0} m ({driven / Math.Max(1f, crow):F1}x crow), " +
                          $"stopped {missedBy:F0} m from the mark, furthest east x={east:F0}, belt={RoadCar.BeltHits}");
    }

    public static void Run()
    {
        TurnWhenOne("empty, getaway ", 0, DriverProfile.Getaway);
        TurnWhenOne("empty, gangster", 0, DriverProfile.Gangster);
        TurnWhenOne("busy,  getaway ", 2, DriverProfile.Getaway);
        TurnWhenOne("busy,  gangster", 5, DriverProfile.Gangster);
        TurnFirstOne();
    }
}
