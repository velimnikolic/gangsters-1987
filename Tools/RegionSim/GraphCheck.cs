using System;
using System.Linq;
using RoadDemo;
using UnityEngine;

// Runs against the real offline-compiled runtime assembly and Unity math types.
// No scene objects, Unity native calls, Editor, or simulation ticks.
class GraphCheck
{
    static LaneNet Square(float x)
    {
        var net = new LaneNet();
        var a = net.AddNode(x, 0, 7.5f, 7.5f);
        var b = net.AddNode(x + 100, 0, 7.5f, 7.5f);
        var c = net.AddNode(x + 100, 100, 7.5f, 7.5f);
        var d = net.AddNode(x, 100, 7.5f, 7.5f);
        var lanes = new[] { 2.5f };
        net.AddRoad(new Vector3(x + 7.5f, 0, 0), new Vector3(x + 92.5f, 0, 0), 7.5f, lanes, 9, a, b, false);
        net.AddRoad(new Vector3(x + 100, 0, 7.5f), new Vector3(x + 100, 0, 92.5f), 7.5f, lanes, 9, b, c, true);
        net.AddRoad(new Vector3(x + 92.5f, 0, 100), new Vector3(x + 7.5f, 0, 100), 7.5f, lanes, 9, c, d, false);
        net.AddRoad(new Vector3(x, 0, 92.5f), new Vector3(x, 0, 7.5f), 7.5f, lanes, 9, d, a, true);
        net.Finish();
        return net;
    }

    static void Main()
    {
        var core = Square(0);
        var district = Square(250);
        var original = district.Roads[0];
        RegionalRoads.Join(core, district.Edges);
        RegionalRoads.Link(core, core.Nodes[1], district.Nodes[0],
            new Vector3(107.5f, 0, 0), new Vector3(242.5f, 0, 0), 9);
        core.Finish();
        if (!core.Roads.Contains(original) || original.Net != core)
            throw new Exception("Original district carriageway was lost.");
        if (!core.RouteToward(district.Edges[0]).ContainsKey(core.Edges[0]) ||
            !core.RouteToward(core.Edges[0]).ContainsKey(district.Edges[0]))
            throw new Exception("Core/district routes are not bidirectional.");
        foreach (var source in core.Edges)
        {
            var reachable = core.ReachableFrom(source);
            foreach (var target in core.Edges)
            {
                core.RouteToward(target, out var distances);
                if (reachable.Contains(target) != distances.ContainsKey(source))
                    throw new Exception("Forward admission disagrees with route search.");
            }
        }
        int roadCount = core.Roads.Count, edgeCount = core.Edges.Count;
        RegionalRoads.Join(core, district.Edges);
        if (core.Roads.Count != roadCount || core.Edges.Count != edgeCount)
            throw new Exception("Registering a district twice duplicated its graph.");
        int queries = 0;
        var height = new TurfHeightField(new Rect(-1000, -2000, 9000, 6000),
            (x, z) => { queries++; return x * 0.01f + z * 0.02f; }, TurfHeightField.RegionalSampleBudget);
        if (queries != height.SampleCount || queries > TurfHeightField.RegionalSampleBudget)
            throw new Exception("Regional terrain cache exceeded its query budget.");
        if (Math.Abs(height.At(125, 750) - 16.25f) > 0.001f)
            throw new Exception("Adaptive terrain interpolation changed an affine height field.");
        Console.WriteLine($"PASSED actual runtime graph: {core.Nodes.Count} nodes, {core.Edges.Count} lanes; " +
            $"bidirectional routing, original carriageways and duplicate registration; terrain {queries} queries. No Unity/Play verdict.");
    }
}
