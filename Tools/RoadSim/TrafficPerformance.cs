using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RoadDemo;
using UnityEngine;

// Offline CPU/allocation measurements, not a Unity rendering/FPS verdict.
// Run against identical frozen source inputs except RoadCar when comparing changes.
static class TrafficPerformance
{
    public static void Run()
    {
        Traffic(40, 90, false, false); // JIT warm-up; do not report this sample.
        foreach (bool mixed in new[] { false, true }) Traffic(200, 1800, mixed, true);
        foreach (int roads in new[] { 32, 149 }) Preview(roads);
        MovingPreview(32, 500f);
        MovingPreview(149, 50f);
    }

    static void MovingPreview(int roads, float segment)
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { 0f }, Enumerable.Range(0, roads + 1).Select(i => i * segment).ToArray(), false);
        var lanes = net.Edges.Where(e => e.Heading == 1).ToList();
        var goal = lanes.Last();
        var cars = new List<RoadCar>();
        var buffers = new List<List<Vector3>>();
        var lastRoad = new Carriageway[16];
        var visibleRoad = new Carriageway[16];
        var visibleAt = new Vector3[16];
        for (int i = 0; i < 16; i++)
        {
            var car = new RoadCar { Net = net, Tf = new Transform() };
            car.Spawn(lanes[i], 10f);
            car.GoTo(goal.Road.Pose(20f, goal.Offset), false);
            cars.Add(car); StreetTraffic.Users.Add(car); buffers.Add(new List<Vector3>());
            lastRoad[i] = car.Road;
        }
        const int frames = 900; // on 10s / off 10s / on 10s
        var times = new List<double>(frames);
        int transitions = 0, sameRoadCatchups = 0, missing = 0, largest = 0;
        long bytes = 0;
        bool wasVisible = false;
        double coldMs = 0, reopenMs = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            Time.deltaTime = 1f / 30f; Time.time = frame / 30f; Time.frameCount = frame;
            RoadCarSimulation.Simulate(cars, Time.deltaTime);
            bool visible = frame < 300 || frame >= 600;
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i].Road != lastRoad[i]) transitions++;
                lastRoad[i] = cars[i].Road;
                if (visible && !wasVisible && frame > 0 && cars[i].Road == visibleRoad[i] &&
                    (cars[i].Position - visibleAt[i]).magnitude > 32f) sameRoadCatchups++;
            }
            if (visible)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < cars.Count; i++)
                    if (!cars[i].CopyPlannedRoute(buffers[i]) && cars[i].HasGoal) missing++;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                times.Add(ms);
                if (frame == 0) coldMs = ms;
                if (frame == 600) reopenMs = ms;
                for (int i = 0; i < cars.Count; i++)
                {
                    visibleRoad[i] = cars[i].Road; visibleAt[i] = cars[i].Position;
                    largest = Math.Max(largest, buffers[i].Count);
                }
            }
            wasVisible = visible;
        }
        times.Sort();
        Console.WriteLine($"PERF moving previews cars=16 roads={roads} segment={segment} visibleFrames={times.Count} " +
            $"cold={coldMs:F3}ms reopen={reopenMs:F3}ms p95={times[(int)(times.Count * .95)]:F3}ms " +
            $"p99={times[(int)(times.Count * .99)]:F3}ms max={times.Last():F3}ms " +
            $"bytes/visible-frame={bytes / times.Count} transitions={transitions} sameRoadCatchups={sameRoadCatchups} maxPoints={largest} missing={missing}");
        TrafficAdmission.Check(missing == 0 && largest <= RoutePreviewBudget.MaxPoints &&
            (segment > 100f ? sameRoadCatchups > 0 : transitions > 0),
            $"moving {roads}-road previews cover {(segment > 100f ? "hidden same-road catch-up" : "crossing rebuilds")} within point budget");
    }

    static void Traffic(int count, int frames, bool mixed, bool report)
    {
        TrafficAdmission.Reset();
        UnityEngine.Random.R = new System.Random(37);
        var axis = new[] { -300f, -150f, 0f, 150f, 300f };
        var net = Program.Grid(axis, axis, true);
        var cars = new List<RoadCar>();
        foreach (var slot in TrafficDistribution.Place(net.Edges, count, 37))
        {
            var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
            if (mixed) TrafficFlow.Body(car, cars.Count);
            car.Spawn(slot.Lane, slot.Progress);
            cars.Add(car); StreetTraffic.Users.Add(car);
        }
        var times = new double[frames];
        long bytes = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            Time.deltaTime = 1f / 30f; Time.time = frame / 30f; Time.frameCount = frame;
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            RoadCarSimulation.Simulate(cars, Time.deltaTime);
            times[frame] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            bytes += GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        }
        if (!report) return;
        Array.Sort(times);
        double checksum = cars.Sum(c => (double)c.Position.x * 17 + c.Position.z * 31);
        Console.WriteLine($"PERF traffic cars={cars.Count} mixed={mixed} frames={frames} " +
            $"p50={times[frames / 2]:F3}ms p95={times[(int)(frames * .95)]:F3}ms " +
            $"p99={times[(int)(frames * .99)]:F3}ms max={times[frames - 1]:F3}ms " +
            $"bytes/frame={bytes / frames} checksum={checksum:F5} recoveries={cars.Sum(c => c.TrafficRecoveries)}");
    }

    static void Preview(int roads)
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { 0f }, Enumerable.Range(0, roads + 1).Select(i => i * 50f).ToArray(), false);
        var first = net.Edges.First(e => e.Heading == 1);
        var last = net.Edges.Last(e => e.Heading == 1);
        var cars = new List<RoadCar>();
        var buffers = new List<List<Vector3>>();
        for (int i = 0; i < 16; i++)
        {
            var car = new RoadCar { Net = net, Tf = new Transform() };
            car.Spawn(first, 10f);
            car.GoTo(last.Road.Pose(20f, last.Offset), park: false);
            cars.Add(car); buffers.Add(new List<Vector3>());
        }
        long coldStart = Stopwatch.GetTimestamp();
        int visible = 0;
        for (int i = 0; i < cars.Count; i++) if (cars[i].CopyPlannedRoute(buffers[i])) visible++;
        double coldMs = (Stopwatch.GetTimestamp() - coldStart) * 1000d / Stopwatch.Frequency;
        const int frames = 1200;
        var times = new double[frames];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < frames; frame++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < cars.Count; i++) cars[i].CopyPlannedRoute(buffers[i]);
            times[frame] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(times);
        Console.WriteLine($"PERF preview cars=16 roads={roads} visible={visible} points/car={buffers[0].Count} " +
            $"cold={coldMs:F3}ms p50={times[frames / 2]:F3}ms p95={times[(int)(frames * .95)]:F3}ms " +
            $"p99={times[(int)(frames * .99)]:F3}ms max={times[frames - 1]:F3}ms bytes/frame={allocated / frames}");
        TrafficAdmission.Check(allocated == 0, $"cached {roads}-road movement previews allocate no managed memory");
    }
}
