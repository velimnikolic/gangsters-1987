using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using RoadDemo;
using UnityEngine;

// Uses the freshly compiled runtime and Unity's managed value types. No engine
// objects, assets, scenes, imports or Editor process are created.
static class Program
{
    static int checks;
    static readonly Color32[] pixels = new Color32[TurfPlate.RW * TurfPlate.RH];

    static void Require(bool pass, string message)
    {
        checks++;
        if (!pass) throw new Exception(message);
    }

    static int Pixel(TurfProjection projection, Vector3 world)
    {
        var p = projection.ToPlan(world) * TurfPlate.S;
        return Mathf.FloorToInt(p.y) * TurfPlate.RW + Mathf.FloorToInt(p.x);
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    static void Main()
    {
        foreach (int yaw in new[] { 0, 90, 180, 270 })
        foreach (float metres in new[] { 0.5f, 1f, 4f })
        {
            var frame = DistrictFrame.At(-410, 370, yaw);
            frame.origin.y = 12f;
            var geometry = new DistrictMapGeometry();
            geometry.Reset(frame);
            // An apron reaches into the harbour basin. Its district envelope must
            // never turn the water beside the pier into concrete.
            geometry.Fill(new Rect(-30, -55, 60, 110), TurfInk.Concrete, 0f);
            geometry.Fill(new Rect(-4, -45, 8, 90), TurfInk.Lane, 0.1f);
            // Composed later, physically lower: must not erase the paint.
            geometry.Fill(new Rect(-20, -50, 40, 100), TurfInk.Road, 0.02f);
            var map = new TurfDistrictMap();
            map.Collect(geometry.Surfaces);
            geometry.Reset(DistrictFrame.Identity);
            geometry.Fill(new Rect(0, 0, 999, 999), TurfInk.Grass, 0f);

            var projection = new TurfProjection(new Rect(-410 - 160 * metres, 370 - 100 * metres,
                320 * metres, 200 * metres));
            var plate = new TurfPlate();
            var water = new bool[TurfPlate.RW * TurfPlate.RH];
            Array.Fill(water, true);
            plate.Clear(TurfInk.Water);
            map.Draw(projection, plate, water);
            plate.Downsample(1, pixels);
            foreach (var sample in new[] {
                (new Vector3(0, 0, 0), TurfInk.Lane, false),
                (new Vector3(13, 0, 0), TurfInk.Road, false),
                (new Vector3(26, 0, 0), TurfInk.Concrete, false),
                (new Vector3(44, 0, 0), TurfInk.Water, true) })
            {
                int at = Pixel(projection, frame.ToWorld(sample.Item1));
                Require(Same(pixels[at], sample.Item2) && water[at] == sample.Item3,
                    $"District surface/mask mismatch at yaw {yaw}, scale {metres}, sample {sample.Item1}");
            }
            Require(geometry.Surfaces.Count == 1 && geometry.Buildings.Count == 0, "Reset leaked old district geometry");
        }

        // A diagonal paved corner may cover its triangle only, not its whole AABB.
        foreach (bool reverse in new[] { false, true })
        {
            var geometry = new DistrictMapGeometry();
            geometry.Reset(DistrictFrame.Identity);
            var a = new Vector3(-100, 0, -100); var b = new Vector3(100, 0, -100);
            var c = new Vector3(-100, 0, 100);
            geometry.Triangle(a, reverse ? c : b, reverse ? b : c, TurfInk.Road);
            // Zero-area polygons and wholly off-sheet geometry must be harmless.
            geometry.Triangle(a, a, a, TurfInk.Lane);
            geometry.Fill(new Rect(10000, 10000, 5, 5), TurfInk.Concrete, 0f);
            var map = new TurfDistrictMap(); map.Collect(geometry.Surfaces);
            var projection = new TurfProjection(new Rect(-80, -50, 160, 100));
            var plate = new TurfPlate(); plate.Clear(TurfInk.Water);
            var water = new bool[TurfPlate.RW * TurfPlate.RH]; Array.Fill(water, true);
            map.Draw(projection, plate, water); plate.Downsample(1, pixels);
            int inside = Pixel(projection, new Vector3(-35, 0, -20));
            int outside = Pixel(projection, new Vector3(35, 0, 20));
            Require(Same(pixels[inside], TurfInk.Road) && !water[inside], "Clipped pavement triangle missing");
            Require(Same(pixels[outside], TurfInk.Water) && water[outside], "Triangle filled empty corner");
        }
        BuildingMassesStayInsidePickBounds();
        GroundFeatureKindsIgnoreQuarterNames();
        Console.WriteLine($"PASSED: {checks} assertions in surface, building-mass and landmark scenarios; offline managed geometry only.");
    }

    static void GroundFeatureKindsIgnoreQuarterNames()
    {
        var collect = typeof(TurfMapSurvey).GetMethod("CollectGroundFeatures",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var survey = new TurfMapSurvey();
        foreach (string quarter in new[] { "Fairgrounds", "Warehouse Row", "Riverside" })
        foreach (var feature in new[] {
            (ResidentialLot.Use.Cafe, "CAFE", TurfLandmarkKind.Cafe, "CAFE"),
            (ResidentialLot.Use.Subway, "SUBWAY", TurfLandmarkKind.Transit, "METRO") })
        {
            var plan = new ResidentialLot.Plan { W = 4, D = 3,
                Ground = new ResidentialLot.Use[4, 3] };
            // Two separate groups exercise the numbered display name as well.
            plan.Ground[0, 0] = feature.Item1;
            plan.Ground[1, 0] = feature.Item1;
            plan.Ground[3, 2] = feature.Item1;
            string name = quarter + " Block 01";
            var recipe = new ResidentialBlockRecipe("test", name,
                new Rect(100, 200, 16, 12), plan, 1, blockId: 7);
            survey.Landmarks.Clear();
            collect.Invoke(survey, new object[] { DistrictFrame.Identity, recipe, feature.Item1, feature.Item2 });
            Require(survey.Landmarks.Count == 2, "Ground feature groups lost");
            for (int i = 0; i < survey.Landmarks.Count; i++)
            {
                var landmark = survey.Landmarks[i];
                Require(landmark.Kind == feature.Item3 && landmark.Label == feature.Item4,
                    $"{landmark.Name} classified as {landmark.Label}");
                Require(landmark.BlockId == 7 && landmark.Name ==
                    (name + ": " + feature.Item2 + (i == 0 ? "" : " 2")).ToUpperInvariant(),
                    "Landmark lost its block identity or display name");
            }
        }
        Require(TurfLandmarkKinds.From("FAIRGROUND") == TurfLandmarkKind.Fairground &&
            TurfLandmarkKinds.Label(TurfLandmarkKind.Fairground) == "FAIR",
            "The real quay fairground lost its label");
    }

    static void BuildingMassesStayInsidePickBounds()
    {
        // Exercise the production rasteriser without creating a Transform/Renderer.
        // The roof overhangs the authored terminal body; fractional edges also catch
        // the raster grid expanding beyond the pick footprint after quantisation.
        var geometry = typeof(TurfMapBuildingLayer).Assembly.GetType("RoadDemo.TurfMapBuildingProxyGeometry");
        var mass = geometry.GetNestedType("SceneMass", BindingFlags.NonPublic);
        var rasterise = geometry.GetMethod("Rasterise", BindingFlags.Static | BindingFlags.NonPublic);
        var bounds = new List<Bounds> {
            new Bounds(new Vector3(30, 4, 20), new Vector3(40, 8, 20)),
            new Bounds(new Vector3(30, 9, 20), new Vector3(70, 2, 40)) };
        var clip = new Rect(10.3f, 10.3f, 39.4f, 19.4f);
        foreach (bool authored in new[] { false, true })
        {
            var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(mass));
            rasterise.Invoke(null, new object[] { bounds, results, authored ? (object)clip : null });
            Require(results.Count > 0, "Terminal masses disappeared");
            bool body = false, outside = false;
            foreach (var result in results)
            {
                var area = (Rect)mass.GetField("Footprint").GetValue(result);
                outside |= area.xMin < clip.xMin || area.yMin < clip.yMin ||
                    area.xMax > clip.xMax + 0.0001f || area.yMax > clip.yMax + 0.0001f;
                body |= (float)mass.GetField("Bottom").GetValue(result) == 0f &&
                    (float)mass.GetField("Top").GetValue(result) == 10f;
            }
            Require(body, "Terminal body was replaced by roof-only mass");
            Require(outside != authored, "Authored clipping changed the wrong mass path");
        }
    }
}
