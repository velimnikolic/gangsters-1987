using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarborDemo;
using RoadDemo;
using UnityEngine;

static class Program
{
    static int cases, shortRuns, samples, coastChecks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static Rect Box(JsonElement e) => new Rect(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

    static int Main(string[] args)
    {
        // Quarter turns, all berth counts, unequal gate offsets and different island sizes.
        foreach (int yaw in new[] { 0, 90, 180, 270 })
            for (int berths = 1; berths <= 6; berths++)
                foreach (float offset in new[] { -340f, 0f, 570f })
                {
                    var city = new Rect(-600f, -450f, 1200f, 900f);
                    var region = new Rect(-1400f, -1800f, 3000f + berths * 100f, 4000f);
                    var turn = DistrictFrame.At(0f, 0f, yaw);
                    var harbor = new HarborDistrict { berths = berths, Frame = new DistrictFrame
                        { origin = turn.ToWorld(new Vector3(offset, 0f, -1100f)), yaw = yaw } };
                    Check(harbor, city, region, new[] { offset, offset + 240f }, 1987 + yaw + berths);
                }
        if (args.Length > 0)
        {
            using var fixtures = JsonDocument.Parse(File.ReadAllText(args[0]));
            foreach (var f in fixtures.RootElement.EnumerateArray())
                foreach (var d in f.GetProperty("districts").EnumerateArray())
                {
                    if ((DistrictKind)d.GetProperty("kind").GetInt32() != DistrictKind.Harbor) continue;
                    var frame = d.GetProperty("frame");
                    var harbor = new HarborDistrict { berths = d.GetProperty("berths").GetInt32(),
                        Frame = DistrictFrame.At(frame[0].GetSingle(), frame[1].GetSingle(), frame[2].GetInt32()) };
                    Check(harbor, Box(f.GetProperty("city")), Box(f.GetProperty("region")), new[] { 0f, 240f },
                        f.GetProperty("seed").GetInt32());
                }
        }
        Require(shortRuns > 0, "fixtures did not reproduce the short regional shipping run");
        Require(coastChecks > cases, "too few dry coastline samples outside the harbor bay");
        Console.WriteLine($"PASSED {cases} harbor layouts, {samples} hull/terrain samples, {coastChecks} preserved-coast samples; " +
            $"{shortRuns} unconfigured runs ended inside the island envelope. " +
            "Actual runtime planning, reservations and terrain triangles; no Unity/Play verdict.");
        return 0;
    }

    static void Check(HarborDistrict harbor, Rect city, Rect region, float[] links, int seed)
    {
        harbor.Plan(links, seed);
        var bounds = IslandLandform.BoundsFor(region);
        float quayShift = (links[0] + links[links.Length - 1]) * .5f;
        float street = (float)typeof(HarborDistrict).GetField("PlannedStreetZ",
            BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        Vector3 World(float x, float z) => harbor.Frame.ToWorld(new Vector3(x + quayShift, HarborDistrict.WaterY, z - street));
        bool Inside(Vector3 p) => bounds.Contains(new Vector2(p.x, p.z));
        float originalRun = Mathf.Max(harbor.seaRun, harbor.QuayHalf + 240f);
        if (Inside(World(-originalRun, -HarborShipping.LaneOffset)) ||
            Inside(World(originalRun, -HarborShipping.LaneOffset))) shortRuns++;

        var previous = new DistrictReservations();
        harbor.Reserve(previous);
        var side = (CityEdge)(harbor.Frame.yaw / 90);
        var oldLand = new IslandLandform(city, region, seed, previous, null, side);
        var reservations = new DistrictReservations();
        reservations.Level(city, RoadDemoBuilder.RoadBed);
        harbor.PlanSeaRoute(bounds, reservations);
        harbor.Reserve(reservations);
        var route = harbor.SeaRoute;
        Require(harbor.seaRun == 240f && route.Run == originalRun, "offshore route enlarged the along-shore reservation");
        var land = new IslandLandform(city, region, seed, reservations, null, side);
        Require(land.Bounds == bounds, "pre-reservation island envelope differs from terrain bounds");

        // Empty prefab lists keep the actual lane assignment entirely managed/offline.
        var shipping = new HarborShipping(harbor, null, new System.Random(seed), new List<GameObject>(),
            null, new List<GameObject>(), null, null, null);
        for (int i = 0; i < harbor.berths; i++) shipping.AddBerth((i - (harbor.berths - 1) * .5f) * harbor.berthPitch, null);
        Require((float)typeof(HarborShipping).GetField("_spawnX", Private).GetValue(shipping) == route.Run,
            "coastal route differs from the reserved regional route");
        var lanes = shipping.Berths.Select(b => b.LaneZ).ToList();
        lanes.Add(HarborShipping.PassingLaneZ(harbor.berths, true));
        lanes.Add(HarborShipping.PassingLaneZ(harbor.berths, false));
        float halfLength = HarborShipSpec.All.Max(s => s.Length) * .5f;
        float halfBeam = HarborShipSpec.All.Max(s => s.Beam) * .5f;
        var radii = lanes.Select(lane => route.BendDepth + lane).OrderBy(r => r).ToArray();
        for (int i = 1; i < radii.Length; i++)
        {
            float sweptOuter = Mathf.Sqrt(Mathf.Pow(radii[i - 1] + halfBeam, 2f) + halfLength * halfLength);
            Require(sweptOuter < radii[i] - halfBeam, "adjacent hull envelopes overlap in the sea-entry bends");
        }
        foreach (float lane in lanes)
        {
            Require(lane + halfBeam < -HarborDistrict.BulkTerminalProjection - HarborDistrict.QuayFace - 3f,
                "coastal shipping hull intersects the reclaimed bulk pier");
            foreach (bool eastbound in new[] { true, false })
            {
                var legs = route.Crossing(lane, harbor.sailSpeed, eastbound).ToArray();
                Require(legs.Length == 5, "missing sea-entry or sea-exit legs");
                for (int l = 0; l < legs.Length; l++)
                {
                    var leg = legs[l];
                    if (l > 0)
                    {
                        legs[l - 1].At(legs[l - 1].Length, out var prev, out var heading);
                        leg.At(0f, out var next, out var nextHeading);
                        Require(Vector3.Distance(prev, next) < .001f && Vector3.Dot(heading, nextHeading) > .999f,
                            "sea-route legs have a position or heading discontinuity");
                    }
                    int count = (int)Math.Ceiling(leg.Length / 20f);
                    for (int i = 0; i <= count; i++)
                    {
                        leg.At(leg.Length * i / count, out var at, out var heading);
                        var right = Vector3.Cross(Vector3.up, heading);
                        foreach (float bow in new[] { -halfLength, halfLength })
                            foreach (float beam in new[] { -halfBeam, halfBeam })
                            {
                                var local = at + heading * bow + right * beam;
                                var p = World(local.x, local.z);
                                if (l == 0 && i == 0 || l == legs.Length - 1 && i == count)
                                    Require(!Inside(p), "a spawn/despawn hull still reaches the terrain envelope");
                                Require(land.Height(p.x, p.z) < HarborDistrict.WaterY - 3f,
                                    $"seed {seed}: dry/shallow shipping route at {p.x},{p.z}");
                                if (Inside(p)) Require(RegionalIslandView.SurfaceHeight(land, p.x, p.z) < HarborDistrict.WaterY - 3f,
                                    $"seed {seed}: terrain triangle crosses a shipping hull at {p.x},{p.z}");
                                samples++;
                            }
                    }
                }
            }
        }
        // Measure land outside the new bay: a water-only assertion cannot detect a
        // long canal or the detached strip of shore that the first fix would create.
        foreach (float sign in new[] { -1f, 1f })
            for (float x = route.Water.xMax + 350f; x < route.Water.xMax + 1350f; x += 50f)
            {
                var p = World(sign * x, -HarborShipping.LaneOffset - 200f);
                if (oldLand.Height(p.x, p.z) <= HarborDistrict.WaterY) continue;
                Require(land.Height(p.x, p.z) > HarborDistrict.WaterY, "open-sea route cut away coast outside the harbor bay");
                coastChecks++;
            }
        // Fixed stay range makes the actual pacing rule deterministic. It must retain
        // the old busy/transit ratio even for routes well beyond the former 4x cap.
        harbor.stayRange = new Vector2(240f, 240f);
        var berth = shipping.Berths[0];
        float stay = (float)typeof(HarborShipping).GetMethod("Stay", Private).Invoke(shipping, new object[] { berth });
        float transit = route.CrossingLength(berth.LaneZ) / harbor.sailSpeed;
        float oldTransit = originalRun * 2f / harbor.sailSpeed;
        Require(stay / transit >= 240f / oldTransit - .001f, "long voyages diluted berth occupancy");
        var outbound = (IEnumerable<HarborShip.Leg>)typeof(HarborShipping).GetMethod("Outbound", Private)
            .Invoke(shipping, new object[] { new Vector3(0, HarborDistrict.WaterY, berth.LaneZ), berth.LaneZ });
        Require(outbound.Count() == 3 && !Inside(World(outbound.Last().B.x, outbound.Last().B.z)),
            "resuming a held departure lost its offshore exit");
        cases++;
    }
}
