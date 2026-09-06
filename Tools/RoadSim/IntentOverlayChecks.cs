using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;

static class IntentOverlayChecks
{
    public static void Run()
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { 0f }, Enumerable.Range(0, 150).Select(i => i * 50f).ToArray(), false);
        var first = net.Edges.First(e => e.Heading == 1);
        var last = net.Edges.Last(e => e.Heading == 1);
        var crew = new DemoCrews.Unit();
        var crews = new DemoCrews();
        var car = new OverlayCar { Net = net, Tf = new Transform(), Occupant = crew };
        car.Spawn(first, 10f);
        car.GoTo(last.Road.Pose(20f, last.Offset), false);
        crews.Cars.Add(car);
        var overlay = new CombatIntentOverlay();
        overlay.Init(crews);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var update = (Action)typeof(CombatIntentOverlay).GetMethod("Update", flags).CreateDelegate(typeof(Action), overlay);
        var lines = (List<LineRenderer>)typeof(CombatIntentOverlay).GetField("_lines", flags).GetValue(overlay);
        update();
        var expected = new List<Vector3>();
        car.CopyPlannedRoute(expected);
        var route = lines[0];
        TrafficAdmission.Check(route.positionCount > 128 && route.BulkCalls == 1 && route.VertexCalls == 0 &&
            Same(route, expected, 0f), "long car indicator submits the full route in one call without changing vertices");
        TrafficAdmission.Check(lines.Count == 2 && lines[1].positionCount == 5 &&
            lines[1].Points[0] == expected.Last() + new Vector3(.45f, 0f, .45f), "destination ring still matches the route end");

        // Run the real Update, not a copied version of the batching helper.
        for (int i = 0; i < 10; i++) update();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1200; i++) update();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        TrafficAdmission.Check(bytes == 0 && route.BulkCalls == 1,
            $"1200 unchanged car-overlay updates allocate {bytes} bytes and leave upload count at {route.BulkCalls}");

        var buffers = (List<Vector3[]>)typeof(CombatIntentOverlay).GetField("_pathPoints", flags).GetValue(overlay);
        var buffer = buffers[0];
        car.GoTo(first.Road.Pose(20f, first.Offset), false);
        update(); car.CopyPlannedRoute(expected);
        TrafficAdmission.Check(ReferenceEquals(buffer, buffers[0]) &&
            route.positionCount == expected.Count && Same(route, expected, 0f), "shorter route reuses capacity without drawing old trailing vertices");

        Keyboard.current = new Keyboard();
        Keyboard.current.iKey.wasPressedThisFrame = true;
        update();
        TrafficAdmission.Check(!overlay.IsVisible && lines.All(l => !l.enabled), "I hides movement lines and destination ring");
        Keyboard.current.iKey.wasPressedThisFrame = false;
        int calls = route.BulkCalls;
        update();
        TrafficAdmission.Check(route.BulkCalls == calls, "hidden indicators do not upload route geometry");
        Keyboard.current.iKey.wasPressedThisFrame = true;
        update();
        TrafficAdmission.Check(overlay.IsVisible && route.enabled && route.BulkCalls == calls,
            "I restores unchanged native geometry without reuploading it");
        Keyboard.current = null;

        crews.Cars.Clear();
        var man = new CrewWalker { Tf = new Transform(), State = CrewWalker.Mode.Walking,
            OrderDestination = new Vector3(20f, 2f, 0f) };
        for (int i = 0; i <= 20; i++) man.Path.Add(new Vector3(i, 2f, 0f));
        crew.Boss = man; crew.Men.Add(man); crews.Units.Add(crew);
        update();
        var trunk = lines[1];
        TrafficAdmission.Check(trunk.BulkCalls == 1 && trunk.positionCount > 2 &&
            Enumerable.Range(0, trunk.positionCount).All(i => Math.Abs(trunk.Points[i].y - 2.12f) < .001f),
            "walking trunk uses one upload and preserves its surface lift");
        crews.Units.Clear(); crews.Cars.Add(car);
        calls = route.BulkCalls;
        update();
        TrafficAdmission.Check(route.BulkCalls == calls + 1 && Same(route, expected, 0f),
            "renderer reused from a walking branch restores the cached car path");
        car.Stop();
    }

    static bool Same(LineRenderer line, List<Vector3> points, float lift)
    {
        if (line.positionCount != points.Count) return false;
        for (int i = 0; i < points.Count; i++) if (line.Points[i] != points[i] + Vector3.up * lift) return false;
        return true;
    }
}
