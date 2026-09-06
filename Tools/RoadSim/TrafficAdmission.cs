using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class TrafficAdmission
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Set(RoadCar car, string field, object value) =>
        typeof(RoadCar).GetField(field, Private).SetValue(car, value);
    internal static object Call(RoadCar car, string method, params object[] args) =>
        typeof(RoadCar).GetMethod(method, Private).Invoke(car, args);
    internal static void Check(bool ok, string message)
    { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {message}"); if (!ok) Environment.ExitCode = 1; }
    internal static void Reset()
    {
        foreach (var car in new List<RoadCar>(RoadCar.All)) car.Vanish();
        StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
        Time.time = 0f; Time.frameCount++;
    }
    internal static void Tick(List<RoadCar> cars, float dt)
    { Time.time += dt; Time.frameCount++; RoadCarSimulation.Simulate(cars, dt); }

    public static void Run()
    {
        foreach (float dt in new[] { 1f / 30f, .2f })
            foreach (int h in new[] { 1, -1 })
            {
                EmptyKerb(h, dt);
                TemporaryReservation(h, dt);
                Opposing(h, dt, 1.5f);
                Opposing(h, dt, 1.5f, large: true);
                Opposing(h, dt, 2.5f);
            }
        FailedOrder();
        Footprints();
        DespawnedReservation();
        PriorityOrder();
        TurningEnvelopes();
    }

    static void EmptyKerb(int h, float dt)
    {
        Reset();
        var net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, new Vector3(0f, 0f, 180f), 7.5f,
            new[] { 2.5f }, 10f, null, null, true);
        net.Finish();
        var car = new KerbApproach.ObservedCar { Net = net, Profile = DriverProfile.Gangster };
        float start = h > 0 ? 160f : 20f;
        car.Spawn(road.LaneFor(h, h * 2.5f), h > 0 ? start : road.Length - start);
        StreetTraffic.Users.Add(car);
        car.GoTo(road.Pose(start, road.KerbD(h, car.HalfWide)), true);
        bool admitted = !car.ParkingFailed;
        var cars = new List<RoadCar> { car };
        for (int i = 0; i < Math.Ceiling(60f / dt) && !car.Parked && !car.ParkingFailed; i++) Tick(cars, dt);
        Check(admitted && car.Parked && car.AtGoal && Math.Abs(car.S - start) <= 45.3f &&
            car.Heading == h && car.TrafficRecoveries == 0,
            $"empty kerb late approach h={h} dt={dt:F3} admitted={admitted} parked={car.Parked} s={car.S:F1} {car.ParkingReason}");
    }

    internal static (LaneNet net, RoadNode node, Carriageway south, Carriageway north) Junction(float offset)
    {
        var net = new LaneNet();
        var node = net.AddNode(0f, 0f, 7.5f, 7.5f, 5.7f);
        var south = net.AddRoad(new Vector3(0f, 0f, -150f), new Vector3(0f, 0f, -7.5f),
            7.5f, new[] { offset }, 10f, null, node, true);
        var north = net.AddRoad(new Vector3(0f, 0f, 7.5f), new Vector3(0f, 0f, 150f),
            7.5f, new[] { offset }, 10f, node, null, true);
        net.Finish(); return (net, node, south, north);
    }

    static void Opposing(int first, float dt, float offset, bool large = false)
    {
        Reset(); var f = Junction(offset);
        var cars = new List<RoadCar>();
        foreach (int h in new[] { first, -first })
        {
            var from = (h > 0 ? f.south : f.north).LaneFor(h, h * offset);
            var to = (h > 0 ? f.north : f.south).LaneFor(h, h * offset);
            var car = new RoadCar { Net = f.net, Profile = DriverProfile.Gangster };
            if (large) { car.HalfLen = 3.8f; car.HalfWide = 1.28f; }
            car.Spawn(from, from.Length - 32f); Set(car, "<Speed>k__BackingField", 14f);
            car.Route = new Dictionary<RoadEdge, RoadEdge> { [from] = to };
            cars.Add(car); StreetTraffic.Users.Add(car);
        }
        float least = 14f; bool together = false, overlap = false;
        for (int i = 0; i < Math.Ceiling(6f / dt); i++)
        {
            Tick(cars, dt);
            together |= cars[0].Via != null && cars[1].Via != null;
            foreach (var car in cars) least = Math.Min(least, car.Speed);
            overlap |= RoadSpace.Overlap(cars[0].Position, cars[0].Forward, cars[0].HalfLen, cars[0].HalfWide,
                cars[1].Position, cars[1].Forward, cars[1].HalfLen, cars[1].HalfWide, 0f, out _);
        }
        Check(together && least > 13.9f && !overlap,
            $"opposing straight first={first} dt={dt:F3} offset={offset} large={large} together={together} min={least:F2} overlap={overlap}");
    }

    static void FailedOrder()
    {
        Reset(); var f = Junction(2.5f);
        var car = new RoadCar { Net = f.net, Profile = DriverProfile.Gangster };
        var lane = f.north.LaneFor(1, 2.5f);
        car.Spawn(lane, 25f); StreetTraffic.Users.Add(car);
        // A real undersized body-to-road fit must fail, but not halt the lane.
        car.HalfWide = 9f;
        car.GoTo(f.north.Pose(80f, 7f), true);
        Check(car.ParkingFailed && !car.Halted && !car.HasGoal && !car.AtGoal && car.ParkingReason.Length > 0,
            $"failed parking releases order, not arrival: {car.ParkingReason}");
        car.HalfWide = .95f;
        float start = car.S;
        for (int i = 0; i < 90; i++) Tick(new List<RoadCar> { car }, 1f / 30f);
        Check(car.S > start + 5f && !car.Parked && car.ParkingFailed, "failed parking keeps moving with visible result");
        car.GoTo(f.north.Pose(100f, 2.5f), false);
        Check(!car.ParkingFailed && car.ParkingReason == "", "new order clears parking failure reason");
    }

    static void Footprints()
    {
        Reset(); var f = Junction(1.5f);
        var a = new RoadCar(); var b = new RoadCar();
        var up = f.node.ConnectorFor(f.south.LaneFor(1, 1.5f), f.north.LaneFor(1, 1.5f));
        var down = f.node.ConnectorFor(f.north.LaneFor(-1, -1.5f), f.south.LaneFor(-1, -1.5f));
        Check(!JunctionClearance.Conflicts(up, a, down, b), "separate body envelopes refine coarse connector conflict");
        a.HalfWide = b.HalfWide = 1.6f;
        Check(JunctionClearance.Conflicts(up, a, down, b), "wide vehicles cannot inherit smaller vehicle clearance");
        for (int i = 0; i < 160; i++)
        {
            a.HalfWide = .9f + i * .001f;
            JunctionClearance.Conflicts(up, a, down, b);
        }
        Check(f.node.BodyClearance.Shapes.Count <= 128 && f.node.BodyClearance.Results.Count <= 512,
            "junction geometry cache stays bounded across varying footprints");
        f.net.Finish();
        Check(f.node.BodyClearance == null, "rebuilding connectors invalidates body clearance cache");
    }

    sealed class ReservedCar : KerbApproach.ObservedCar
    {
        internal bool Reserved = true;
        protected override bool ParkingSpotAvailable(Vector3 at) => !Reserved;
    }

    static void TemporaryReservation(int h, float dt)
    {
        Reset(); var f = Junction(2.5f); var road = f.north;
        float start = h > 0 ? 35f : 110f, target = h > 0 ? 85f : 60f;
        var car = new ReservedCar { Net = f.net, Profile = DriverProfile.Gangster };
        car.Spawn(road.LaneFor(h, h * 2.5f), h > 0 ? start : road.Length - start);
        StreetTraffic.Users.Add(car);
        car.GoTo(road.Pose(target, road.KerbD(h, car.HalfWide)), true);
        bool waiting = car.HasGoal && !car.ParkingFailed && car.ParkingReason.Contains("reservation");
        var cars = new List<RoadCar> { car };
        for (int i = 0; i < Math.Ceiling(35f / dt) && !car.Parked && !car.ParkingFailed; i++)
        { if (Time.time > 2f) car.Reserved = false; Tick(cars, dt); }
        Check(waiting && car.Parked && car.AtGoal && !car.Overlapped && car.Discontinuities == 0 &&
            Math.Abs(car.S - target) < 45.3f,
            $"temporary reservation retries h={h} dt={dt:F3} waiting={waiting} parked={car.Parked} {car.ParkingReason}");
    }

    static void DespawnedReservation()
    {
        Reset(); var f = Junction(2.5f); var lane = f.north.LaneFor(1, 2.5f);
        var a = new RoadCar { Net = f.net, Profile = DriverProfile.Gangster };
        var b = new RoadCar { Net = f.net, Profile = DriverProfile.Gangster };
        a.Spawn(lane, 15f); b.Spawn(lane, 5f);
        var target = f.north.Pose(80f, f.north.KerbD(1, a.HalfWide));
        a.GoTo(target, true); a.Despawn(); b.GoTo(target, true);
        float selected = (float)typeof(RoadCar).GetField("_goalS", Private).GetValue(b);
        Check(!b.ParkingFailed && Math.Abs(selected - 80f) < .01f, "despawn releases remote parking destination");
    }

    static void PriorityOrder()
    {
        Reset();
        var via = new Connector { Length = 100f };
        var cars = new[] { new RoadCar { Profile = DriverProfile.Police },
            new RoadCar { Profile = DriverProfile.Gangster }, new RoadCar { Profile = DriverProfile.Traffic } };
        bool cycle = false, asymmetric = true;
        foreach (float origin in new[] { 0f, .019f, .5f })
        {
            float[] progress = { origin, origin + .015f, origin + .03f };
            bool[,] yields = new bool[3, 3];
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) if (a != b)
                yields[a, b] = (bool)Call(cars[a], "YieldsInBox",
                    new NodeOccupant { Car = cars[b], Via = via, S = progress[b] * via.Length }, progress[a]);
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) if (a != b)
                asymmetric &= yields[a, b] != yields[b, a];
            cycle |= yields[0, 1] && yields[1, 2] && yields[2, 0] || yields[0, 2] && yields[2, 1] && yields[1, 0];
        }
        Check(asymmetric && !cycle, "junction priority remains transitive near progress thresholds");
    }

    static void TurningEnvelopes()
    {
        Reset();
        var net = new LaneNet(); var node = net.AddNode(0f, 0f, 7.5f, 7.5f, 5.7f);
        net.AddRoad(new Vector3(0f, 0f, -150f), new Vector3(0f, 0f, -7.5f), 7.5f, new[] { 2.5f }, 10f, null, node, true);
        net.AddRoad(new Vector3(0f, 0f, 7.5f), new Vector3(0f, 0f, 150f), 7.5f, new[] { 2.5f }, 10f, node, null, true);
        net.AddRoad(new Vector3(-150f, 0f, 0f), new Vector3(-7.5f, 0f, 0f), 7.5f, new[] { 2.5f }, 10f, null, node, false);
        net.AddRoad(new Vector3(7.5f, 0f, 0f), new Vector3(150f, 0f, 0f), 7.5f, new[] { 2.5f }, 10f, node, null, false);
        net.Finish();
        foreach (float length in new[] { 2.3f, 3.8f })
        {
            var car = new RoadCar { Net = net, HalfLen = length, HalfWide = 1.1f, AxleBack = length * .7f };
            var other = new RoadCar { Net = net, HalfLen = length, HalfWide = 1.1f, AxleBack = length * .7f };
            int checkedPairs = 0, separatedTurns = 0; bool overlap = false, symmetric = true;
            var samples = new Dictionary<Connector, List<(Vector3 p, Vector3 f)>>();
            foreach (var connector in node.Connectors)
            {
                var poses = new List<(Vector3 p, Vector3 f)>();
                for (float s = -length; s <= connector.Length + length; s += .1f)
                {
                    JunctionClearance.Pose(connector, s, car.CrossingAxle, out var p, out var f);
                    poses.Add((p, f));
                }
                samples[connector] = poses;
            }
            for (int i = 0; i < node.Connectors.Count; i++) for (int j = i + 1; j < node.Connectors.Count; j++)
            {
                var a = node.Connectors[i]; var b = node.Connectors[j];
                if (a.From == b.From) continue;
                bool conflict = JunctionClearance.Conflicts(a, car, b, other);
                symmetric &= conflict == JunctionClearance.Conflicts(b, other, a, car);
                if (conflict) continue;
                checkedPairs++;
                if (a.Kind != Turn.Straight || b.Kind != Turn.Straight) separatedTurns++;
                foreach (var pa in samples[a]) foreach (var pb in samples[b])
                    overlap |= RoadSpace.Overlap(pa.p, pa.f, length, 1.1f, pb.p, pb.f, length, 1.1f, .06f, out _);
            }
            Check(checkedPairs > 0 && separatedTurns > 0 && !overlap && symmetric,
                $"body envelope dense sweep halfLength={length} clearPairs={checkedPairs} clearTurns={separatedTurns} overlap={overlap}");
        }
    }
}
