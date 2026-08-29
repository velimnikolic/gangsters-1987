using System.Collections.Generic;
using UnityEngine;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>Pure geometry checks for every parking shape used by ParkingDemo.</summary>
    public static class ParkingBlockTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            FullLotHasSeveralAisles(failures);
            ShallowPocketStillWorks(failures);
            BuildingFootprintIsRespected(failures);
            CentralDriveStaysOpen(failures);
            GateThroatIsStraight(failures);
            UrbanBlockUsesCorePavement(failures);
            CoreAmenityCountIsCapped(failures);
            CoreFuelSurfaceStaysInsideParcel(failures);
            return failures;
        }

        static void FullLotHasSeveralAisles(List<string> failures)
        {
            var plan = ParkingBlockPlan.Generate(60f, 40f);
            if (plan.Stalls.Count < 20)
                failures.Add($"Parking block: a 60x40 independent lot produced only {plan.Stalls.Count} bays");

            bool near = false, deep = false;
            foreach (var stall in plan.Stalls)
            {
                if (stall.Junction.z < 8f) near = true;
                if (stall.Junction.z > 15f) deep = true;
            }
            if (!near || !deep)
                failures.Add("Parking block: the full lot does not serve bays from multiple aisles");
        }

        static void ShallowPocketStillWorks(List<string> failures)
        {
            var plan = ParkingBlockPlan.Generate(55f, 18f);
            if (plan.Stalls.Count < 8)
                failures.Add($"Parking block: a shallow embedded strip produced only {plan.Stalls.Count} bays");
            foreach (var stall in plan.Stalls)
                if (stall.Stand.z + ParkingBlockPlan.StallDepth * 0.5f > plan.Depth + 0.01f)
                    failures.Add("Parking block: a shallow strip has a bay outside its footprint");
        }

        static void BuildingFootprintIsRespected(List<string> failures)
        {
            var building = new Rect(0f, 20f, 25f, 20f);
            var plan = ParkingBlockPlan.Generate(65f, 40f, new[] { building });
            foreach (var stall in plan.Stalls)
            {
                var bay = Rect.MinMaxRect(
                    stall.Stand.x - ParkingBlockPlan.StallWidth * 0.5f,
                    stall.Stand.z - ParkingBlockPlan.StallDepth * 0.5f,
                    stall.Stand.x + ParkingBlockPlan.StallWidth * 0.5f,
                    stall.Stand.z + ParkingBlockPlan.StallDepth * 0.5f);
                if (building.Overlaps(bay))
                    failures.Add("Parking block: an L-shaped lot placed a bay inside its building footprint");
            }
            if (plan.ContainsSurface(building.center))
                failures.Add("Parking block: the building footprint was still classified as parking surface");
        }

        static void CentralDriveStaysOpen(List<string> failures)
        {
            var plan = ParkingBlockPlan.Generate(60f, 40f);
            float centre = plan.Width * 0.5f;
            foreach (var stall in plan.Stalls)
                if (Mathf.Abs(stall.Stand.x - centre) < ParkingBlockPlan.GateWidth * 0.5f)
                    failures.Add("Parking block: a bay overlaps the central entry drive");
        }

        static void GateThroatIsStraight(List<string> failures)
        {
            var plan = ParkingBlockPlan.Generate(65f, 45f);
            var curve = PatrolDocking.Sweep(
                plan.GateOutside, Vector3.forward, plan.GateInside, Vector3.forward);
            for (int i = 0; i <= 100; i++)
            {
                var point = PatrolDocking.Point(curve, i / 100f);
                if (Mathf.Abs(point.x - plan.Gate.x) <= 0.001f) continue;
                failures.Add("Parking block: a car drifts sideways through the payment gate");
                break;
            }
        }

        static void UrbanBlockUsesCorePavement(List<string> failures)
        {
            var block = new Rect(10f, 20f, 70f, 50f);
            var surface = ParkingBlockSite.Surface(block, ParkingBlockStyle.UrbanBlock);
            float want = CoreBlockMetrics.PavementWidth;
            if (Mathf.Abs(surface.xMin - block.xMin - want) > 0.001f ||
                Mathf.Abs(surface.yMin - block.yMin - want) > 0.001f ||
                Mathf.Abs(block.xMax - surface.xMax - want) > 0.001f ||
                Mathf.Abs(block.yMax - surface.yMax - want) > 0.001f)
                failures.Add("Parking block: urban pavement is not the shared 10 m CoreDemo width");
        }

        static void CoreAmenityCountIsCapped(List<string> failures)
        {
            var raster = new CoreRoads.Raster
            {
                X0 = 0f,
                Z0 = 0f,
                NX = 40,
                NZ = 24,
                Kinds = new CoreRoads.Kind[40, 24],
            };
            var boxes = new[]
            {
                new Rect(10f, 10f, 90f, 35f),
                new Rect(110f, 10f, 70f, 40f),
                new Rect(10f, 80f, 70f, 40f),
            };
            foreach (var box in boxes)
            {
                int i0 = Mathf.RoundToInt(box.xMin / CoreRoads.Cell);
                int i1 = Mathf.RoundToInt(box.xMax / CoreRoads.Cell);
                int j0 = Mathf.RoundToInt(box.yMin / CoreRoads.Cell);
                int j1 = Mathf.RoundToInt(box.yMax / CoreRoads.Cell);
                for (int i = i0; i < i1; i++)
                {
                    raster.Kinds[i, j0 - 1] = CoreRoads.Kind.StreetEW;
                    for (int j = j0; j < j1; j++)
                        raster.Kinds[i, j] = CoreRoads.Kind.Parking;
                }
            }

            var parking = new List<CoreAmenityLayout.Site>();
            var fuel = new List<CoreAmenityLayout.Site>();
            var development = new List<CoreAmenityLayout.Site>();
            CoreAmenityLayout.Select(raster, boxes, 1987, 1, 1,
                parking, fuel, development);
            if (parking.Count != 1 || fuel.Count != 1)
                failures.Add($"Core amenities: cap 1+1 selected {parking.Count} parking and {fuel.Count} fuel");
            if (development.Count != 2)
                failures.Add($"Core amenities: expected unused parcel plus cropped fuel remainder, got {development.Count}");
            if (parking.Count > 0 && fuel.Count > 0 && parking[0].Box.Overlaps(fuel[0].Box))
                failures.Add("Core amenities: one remainder parcel was assigned to parking and fuel");
            if (fuel.Count > 0)
            {
                if (Mathf.Abs(fuel[0].Box.width - CoreAmenityLayout.FuelFrontage) > 0.01f &&
                    Mathf.Abs(fuel[0].Box.height - CoreAmenityLayout.FuelFrontage) > 0.01f)
                    failures.Add($"Core amenities: fuel parcel did not crop to a " +
                                 $"{CoreAmenityLayout.FuelFrontage:F0} m road frontage");
                CoreAmenityLayout.FuelPose(fuel[0], out var anchor, out _);
                if (!fuel[0].Box.Contains(new Vector2(anchor.x, anchor.z)))
                    failures.Add("Core amenities: PumpDemo anchor falls outside its assigned parcel");
            }
            foreach (var site in development)
            {
                if (parking.Contains(site) || fuel.Contains(site))
                    failures.Add("Core amenities: a parcel is both developed and retained as an amenity");
                int w = Mathf.RoundToInt(site.Box.width / CoreLayout.Cell);
                int d = Mathf.RoundToInt(site.Box.height / CoreLayout.Cell);
                if (ResidentialLot.Classify(
                        w - 2 * ResidentialLot.Walk,
                        d - 2 * ResidentialLot.Walk) == null)
                    failures.Add($"Core amenities: fuel left an undevelopable {w}x{d} cell remainder");
            }
        }

        static void CoreFuelSurfaceStaysInsideParcel(List<string> failures)
        {
            var box = new Rect(10f, 20f, 80f, 60f);
            foreach (var entry in new[]
            {
                ParkingEntrySide.South,
                ParkingEntrySide.East,
                ParkingEntrySide.North,
                ParkingEntrySide.West,
            })
            {
                var surface = CoreAmenityLayout.FuelSurface(
                    new CoreAmenityLayout.Site(box, entry, 1));
                if (Mathf.Abs(surface.xMin - box.xMin) > 0.001f ||
                    Mathf.Abs(surface.xMax - box.xMax) > 0.001f ||
                    Mathf.Abs(surface.yMin - box.yMin) > 0.001f ||
                    Mathf.Abs(surface.yMax - box.yMax) > 0.001f)
                    failures.Add($"Core amenities: {entry} PumpDemo does not cover its whole assigned parcel");
            }
        }
    }
}
