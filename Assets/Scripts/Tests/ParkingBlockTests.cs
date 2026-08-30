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
            FullFuelBlockPoseMatchesEveryEntry(failures);
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
                NX = 50,
                NZ = 50,
                Kinds = new CoreRoads.Kind[50, 50],
            };
            var boxes = new[]
            {
                new Rect(10f, 10f, 80f, 70f),
                new Rect(110f, 10f, 70f, 60f),
                new Rect(10f, 110f, 70f, 60f),
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
            if (development.Count != 1)
                failures.Add($"Core amenities: expected one unused whole parcel, got {development.Count}");
            if (parking.Count > 0 && fuel.Count > 0 && parking[0].Box.Overlaps(fuel[0].Box))
                failures.Add("Core amenities: one remainder parcel was assigned to parking and fuel");
            if (fuel.Count > 0)
            {
                bool side = fuel[0].Entry == ParkingEntrySide.East ||
                            fuel[0].Entry == ParkingEntrySide.West;
                float frontage = side ? fuel[0].Box.height : fuel[0].Box.width;
                float depth = side ? fuel[0].Box.width : fuel[0].Box.height;
                if (Mathf.Abs(frontage - FuelStationBlock.BlockFrontage) > 0.001f ||
                    Mathf.Abs(depth - FuelStationBlock.BlockDepth) > 0.001f)
                    failures.Add($"Core amenities: full PumpDemo block is {frontage}x{depth}, " +
                                 $"not {FuelStationBlock.BlockFrontage}x{FuelStationBlock.BlockDepth}");

                bool insideSource = false;
                for (int i = 0; i < boxes.Length; i++)
                    if (Contains(boxes[i], fuel[0].Box)) insideSource = true;
                if (!insideSource)
                    failures.Add("Core amenities: cropped PumpDemo block falls outside every source parcel");
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
                    failures.Add($"Core amenities: unused whole parcel is undevelopable at {w}x{d} cells");
            }
        }

        static bool Same(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 0.01f &&
            Mathf.Abs(a.yMin - b.yMin) < 0.01f &&
            Mathf.Abs(a.width - b.width) < 0.01f &&
            Mathf.Abs(a.height - b.height) < 0.01f;

        static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - 0.001f && inner.xMax <= outer.xMax + 0.001f &&
            inner.yMin >= outer.yMin - 0.001f && inner.yMax <= outer.yMax + 0.001f;

        static void FullFuelBlockPoseMatchesEveryEntry(List<string> failures)
        {
            foreach (var entry in new[]
            {
                ParkingEntrySide.South,
                ParkingEntrySide.East,
                ParkingEntrySide.North,
                ParkingEntrySide.West,
            })
            {
                bool side = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
                var box = new Rect(10f, 20f,
                    side ? FuelStationBlock.BlockDepth : FuelStationBlock.BlockFrontage,
                    side ? FuelStationBlock.BlockFrontage : FuelStationBlock.BlockDepth);
                var site = new CoreAmenityLayout.Site(box, entry, 1);
                CoreAmenityLayout.FuelBlockPose(site, out var position, out int yaw);
                var placed = PlacedFuelBounds(position, yaw);
                if (!Same(placed, box))
                    failures.Add($"Core amenities: {entry} full PumpDemo pavement bounds " +
                                 $"{placed} do not match reserved block {box}");
            }
        }

        static Rect PlacedFuelBounds(Vector3 position, int yaw)
        {
            var area = FuelStationBlock.PreviewBounds;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var corners = new[]
            {
                position + turn * new Vector3(area.xMin, 0f, area.yMin),
                position + turn * new Vector3(area.xMin, 0f, area.yMax),
                position + turn * new Vector3(area.xMax, 0f, area.yMin),
                position + turn * new Vector3(area.xMax, 0f, area.yMax),
            };
            float x0 = corners[0].x, x1 = corners[0].x;
            float z0 = corners[0].z, z1 = corners[0].z;
            for (int i = 1; i < corners.Length; i++)
            {
                x0 = Mathf.Min(x0, corners[i].x);
                x1 = Mathf.Max(x1, corners[i].x);
                z0 = Mathf.Min(z0, corners[i].z);
                z1 = Mathf.Max(z1, corners[i].z);
            }
            return Rect.MinMaxRect(x0, z0, x1, z1);
        }
    }
}
