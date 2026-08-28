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
    }
}
