using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEngine;

static class PowerlineChecks
{
    public static int Run(List<CoreLayout.Block> blocks, int seed, int count)
    {
        int failures = 0, scenarios = 0;
        foreach (bool vertical in new[] { false, true })
            foreach (int width in new[] { 1, 2, 3, 7 })
                foreach (var obstruction in new[]
                {
                    (from: 20f, to: 30f, kind: CoreRoads.Kind.Bare, poles: 4, wires: 9),
                    // The pole at 44 m is off the road, but its clearance reaches it.
                    (from: 45f, to: 50f, kind: CoreRoads.Kind.Bare, poles: 4, wires: 9),
                    (from: 20f, to: 30f, kind: CoreRoads.Kind.Parking, poles: 4, wires: 9),
                    (from: 20f, to: 70f, kind: CoreRoads.Kind.Water, poles: 2, wires: 0),
                })
            {
                // Two stretches merged across a ten-metre cross street. The old
                // 21 m rhythm puts a pole at 23 m, in that street, in either orientation.
                var raster = new CoreRoads.Raster
                {
                    X0 = -50f, Z0 = -50f, NX = 40, NZ = 40,
                    Kinds = new CoreRoads.Kind[40, 40],
                };
                for (int i = 0; i < raster.NX; i++)
                    for (int j = 0; j < raster.NZ; j++)
                    {
                        float along = vertical ? raster.Z(j) : raster.X(i);
                        float across = vertical ? raster.X(i) : raster.Z(j);
                        raster.Kinds[i, j] = along >= obstruction.from && along < obstruction.to
                            ? obstruction.kind
                            : across >= 0f && across < width * 5f
                                ? (vertical ? CoreRoads.Kind.StreetNS : CoreRoads.Kind.StreetEW)
                                : CoreRoads.Kind.Block;
                    }
                foreach (var ends in new[] { (0f, 20f), (30f, 90f) })
                    raster.Stretches.Add(new CoreRoads.Stretch
                    {
                        Vertical = vertical, Width = width, Crown = width * 2.5f,
                        From = ends.Item1, To = ends.Item2,
                    });
                failures += Check(raster, null, seed,
                    $"cross street {vertical}/{width}/{obstruction.kind}/{obstruction.from}",
                    obstruction.poles, obstruction.wires);
                scenarios++;
            }
        for (int n = 0; n < count; n++)
        {
            var plan = CoreLayout.Arrange(blocks, seed + n, out var raster);
            failures += Check(raster, plan, seed + n, $"generated seed {seed + n}");
            scenarios++;
        }
        Console.WriteLine($"Powerlines: {scenarios} scenarios, {failures} failures (offline geometry only).");
        return failures == 0 ? 0 : 1;
    }

    static int Check(CoreRoads.Raster raster, CoreLayout.Plan plan, int seed, string label,
                     int expectedPoles = -1, int expectedWires = -1)
    {
        var spawned = new List<GameObject>();
        CorePowerlines.Stand(plan, raster, new GameObject("test").transform, seed,
            (prefab, parent) =>
            {
                var go = new GameObject(prefab.name);
                go.transform.SetParent(parent, false);
                spawned.Add(go);
                return go;
            });
        var poles = spawned.Where(p => p.name.Contains("Powerpole")).ToList();
        int failures = poles.Count == 0 ? 1 : 0;
        if (expectedPoles >= 0 && poles.Count != expectedPoles) failures++;
        if (expectedWires >= 0 && spawned.Count - poles.Count != expectedWires) failures++;
        foreach (var pole in poles)
        {
            var p = pole.transform.position;
            // Independently intersect the clearance box with every road/water cell.
            bool blocked = false;
            for (int i = 0; i < raster.NX; i++)
                for (int j = 0; j < raster.NZ; j++)
                {
                    var kind = raster.At(i, j);
                    if (!CoreRoads.IsRoad(kind) && kind != CoreRoads.Kind.Water) continue;
                    blocked |= p.x + 1.25f >= raster.X(i) && p.x - 1.25f < raster.X(i) + 5f &&
                               p.z + 1.25f >= raster.Z(j) && p.z - 1.25f < raster.Z(j) + 5f;
                }
            if (blocked) failures++;
        }
        foreach (var wire in spawned.Where(p => p.name.Contains("Powerline_")))
        {
            var t = wire.transform;
            var end = t.position + t.rotation * Vector3.forward * (t.localScale.z * 7.696f);
            bool Supported(Vector3 p) => poles.Any(pole =>
                Math.Abs(pole.transform.position.x - p.x) <= 0.86f &&
                Math.Abs(pole.transform.position.z - p.z) <= 0.86f);
            if (!Supported(t.position) || !Supported(end) || t.localScale.z * 7.696f > 42.01f)
                failures++;
        }
        Console.WriteLine($"  {label}: poles {poles.Count}, failures {failures}");
        return failures;
    }
}
