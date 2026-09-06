using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class RegionalTraffic
{
    static void Check(bool ok, string label)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}");
        if (!ok) Environment.ExitCode = 1;
    }

    static void Reset()
    {
        StreetTraffic.Users.Clear(); StreetTraffic.Walkers.Clear(); StreetTraffic.Bodies.Clear();
        Time.time = 0f; Time.frameCount++;
    }

    public static void Run()
    {
        foreach (float level in new[] { 0f, 7f })
            foreach (float personLevel in new[] { 0f, .25f, 7f })
                foreach (bool body in new[] { false, true })
                    foreach (bool junction in new[] { false, true })
                        People(level, personLevel, body, junction);
        foreach (float level in new[] { 0f, 7f })
            foreach (float personLevel in new[] { 0f, 7f })
                foreach (bool body in new[] { false, true }) Reverse(level, personLevel, body);
        foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
            foreach (float dt in new[] { 1f / 30f, .2f }) Portal(direction, dt);
        foreach (float level in new[] { 0f, 7f })
            foreach (float otherLevel in new[] { 0f, 7f }) PhysicalLevels(level, otherLevel);
        Hill();
    }

    static void People(float level, float personLevel, bool body, bool junction)
    {
        Reset();
        var net = new LaneNet();
        var node = junction ? net.AddNode(0f, 45f, 5f, 5f, 0f) : null;
        var road = net.AddRoad(Vector3.zero, Vector3.forward * (junction ? 40f : 200f), 5f,
            new[] { 2.5f }, 10f, null, node, true);
        road.SurfaceY = level;
        Carriageway exit = null;
        if (junction)
        {
            exit = net.AddRoad(Vector3.forward * 50f, Vector3.forward * 200f, 5f,
                new[] { 2.5f }, 10f, node, null, true);
            exit.SurfaceY = level;
        }
        net.Finish();
        var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
        car.Spawn(road.LaneFor(1, 2.5f), 30f);
        if (junction) car.Route = new Dictionary<RoadEdge, RoadEdge>
            { { road.LaneFor(1, 2.5f), exit.LaneFor(1, 2.5f) } };
        StreetTraffic.Users.Add(car);
        var person = new Vector3(2.5f, personLevel, 45f);
        if (body) StreetTraffic.Bodies.Add(new StreetTraffic.Body(person, 0));
        else StreetTraffic.Walkers.Add(person);
        var cars = new List<RoadCar> { car };
        // Within the body-wait grace period: a person on the road must stop us.
        for (int i = 0; i < 150; i++)
        {
            Time.time += 1f / 30f; Time.frameCount++;
            RoadCarSimulation.Simulate(cars, 1f / 30f);
        }
        bool separate = Math.Abs(level - personLevel) > RoadSpace.Storey;
        Check(separate ? car.Position.z > 48f : car.Position.z < 42f && car.Speed < .2f,
            $"person height road={level} person={personLevel} body={body} junction={junction} z={car.Position.z:F2} speed={car.Speed:F2}");
        car.Vanish();
    }

    static void Reverse(float level, float personLevel, bool body)
    {
        Reset();
        var net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, Vector3.forward * 100f, 5f,
            new[] { 2.5f }, 10f, null, null, true);
        road.SurfaceY = level; net.Finish();
        var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
        car.Spawn(road.LaneFor(1, 2.5f), 50f);
        var person = new Vector3(2.5f, personLevel, 44f);
        if (body) StreetTraffic.Bodies.Add(new StreetTraffic.Body(person, 0));
        else StreetTraffic.Walkers.Add(person);
        float room = (float)typeof(RoadCar).GetMethod("ClearBehind", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(car, new object[] { 10f });
        Check(Math.Abs(level - personLevel) > RoadSpace.Storey ? room > 9.9f : room < 3f,
            $"reverse person road={level} person={personLevel} body={body} room={room:F1}");
        car.Vanish();
    }

    static void Portal(Vector3 direction, float dt)
    {
        Reset();
        var net = new LaneNet();
        var node = net.AddNode(0f, 0f, 5f, 5f, 0f);
        var right = Vector3.Cross(Vector3.up, direction);
        // SuburbDistrict.BuildPortals ends legacy lanes at the portal centre;
        // the region starts its ordinary two-way road at that same centre.
        var from = new RoadEdge { From = null, To = node, Start = -direction * 100f + right * 2.5f,
            End = right * 2.5f, Dir = direction, Length = 100f, SpeedLimit = 10f };
        node.Incoming.Add(from); net.Adopt(from);
        var back = new RoadEdge { From = node, To = null, Start = -right * 2.5f,
            End = -direction * 100f - right * 2.5f, Dir = -direction, Length = 100f, SpeedLimit = 10f };
        node.Outgoing.Add(back); net.Adopt(back);
        var road = net.AddRoad(Vector3.zero, direction * 100f, 5f, new[] { 2.5f },
            10f, node, null, Math.Abs(direction.z) > .5f);
        net.Finish();
        var to = road.LaneFor(1, 2.5f);
        var connector = node.Connectors.Find(c => c.From == from && c.To == to);
        connector.Pose(0f, out _, out var forward);
        Check(Vector3.Dot(forward, direction) > .999f, $"portal tangent dir={direction} dt={dt}");
        var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic, HalfLen = 4.5f, HalfWide = 1.25f };
        car.Spawn(from, 70f); car.Route = new Dictionary<RoadEdge, RoadEdge> { { from, to } };
        StreetTraffic.Users.Add(car);
        var opposite = road.LaneFor(-1, -2.5f);
        var peer = new RoadCar { Net = net, Profile = DriverProfile.Traffic, HalfLen = 4.5f, HalfWide = 1.25f };
        peer.Spawn(opposite, 70f); peer.Route = new Dictionary<RoadEdge, RoadEdge> { { opposite, back } };
        StreetTraffic.Users.Add(peer);
        var returning = node.Connectors.Find(c => c.From == opposite && c.To == back);
        Check(!JunctionClearance.Conflicts(connector, car, returning, peer), $"portal opposing envelopes dir={direction} dt={dt}");
        var cars = new List<RoadCar> { car, peer };
        float worst = 0f;
        for (int i = 0; i < Math.Ceiling(10f / dt); i++)
        {
            Time.time += dt; Time.frameCount++;
            RoadCarSimulation.Simulate(cars, dt);
            worst = Math.Max(worst, Vector3.Angle(car.Forward, direction));
            if (car.Road == road && car.Progress > 15f) break;
        }
        Check(car.Road == road && car.Progress > 15f && worst < 1f,
            $"portal drive dir={direction} dt={dt} worstYaw={worst:F1} progress={car.Progress:F1} why={car.Why}");
        car.Vanish();
        peer.Vanish();
    }

    static void PhysicalLevels(float level, float otherLevel)
    {
        Reset();
        var net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, Vector3.forward * 100f, 5f,
            new[] { 2.5f }, 10f, null, null, true);
        var otherRoad = net.AddRoad(Vector3.zero, Vector3.forward * 100f, 5f,
            new[] { 2.5f }, 10f, null, null, true);
        road.SurfaceY = level; otherRoad.SurfaceY = otherLevel; net.Finish();
        var car = new RoadCar { Net = net };
        var peer = new RoadCar { Net = net };
        car.Spawn(road.LaneFor(1, 2.5f), 50f);
        peer.Spawn(otherRoad.LaneFor(1, 2.5f), 50f);
        StreetTraffic.Users.Add(car); StreetTraffic.Users.Add(peer);
        var hit = RoadSpace.Inside(car, car.Position, car.Forward, car.HalfLen, car.HalfWide, out _);
        Check(level == otherLevel ? hit == peer : hit == null,
            $"physical road levels self={level} other={otherLevel} blocked={hit != null}");
        car.Vanish(); peer.Vanish();
    }

    static void Hill()
    {
        Reset();
        var net = new LaneNet();
        var edge = new RoadEdge { Start = Vector3.zero, End = Vector3.forward * 100f,
            Dir = Vector3.forward, Length = 100f, SpeedLimit = 9f };
        var road = net.Adopt(edge);
        road.SurfaceAt = s => 4f + s * .02f;
        net.Finish();
        var local = new DemoVehicle { Net = net };
        var visitor = new RoadCar { Net = net };
        local.Spawn(edge, 30f); visitor.Spawn(edge, 30f);
        Check(Math.Abs(local.RoadPosition.y - 4.6f) < .001f &&
            Math.Abs(local.RoadPosition.y - visitor.RoadPosition.y) < .001f,
            "adopted hillside surface shared by local and visiting drivers");
        local.Vanish();
        StreetTraffic.Users.Add(visitor);
        StreetTraffic.Walkers.Add(new Vector3(0f, 4.9f, 45f));
        var cars = new List<RoadCar> { visitor };
        for (int i = 0; i < 150; i++)
        {
            Time.time += 1f / 30f; Time.frameCount++;
            RoadCarSimulation.Simulate(cars, 1f / 30f);
        }
        Check(visitor.Position.z < 42f && visitor.Speed < .2f,
            $"hillside pedestrian stops visiting car z={visitor.Position.z:F2}");
        visitor.Vanish();
    }
}
