using System.Collections.Generic;
using HarborDemo;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>Pure planning checks for the port-owned skyline. Asset measurement and
    /// renderer checks belong to the editor audit; these keep the district boundary and
    /// the ordinary industrial strip from quietly absorbing the landmarks again.</summary>
    public static class HarborLandmarkTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            HarborOwnsAnAsymmetricBulkPier(failures);
            SilosAreSkylineScale(failures);
            IndustrialStripKeepsItsOriginalDeal(failures);
            return failures;
        }

        static void HarborOwnsAnAsymmetricBulkPier(List<string> failures)
        {
            var harbor = new HarborDistrict { berths = 5 };
            harbor.Plan(null, 1987);

            if (HarborDistrict.BulkTerminalLength < 100f)
                failures.Add("bulk terminal does not widen the harbor by at least 100 m");
            if (HarborDistrict.BulkTerminalProjection < 15f)
                failures.Add("bulk pier does not project far enough into the basin");
            if (harbor.PlannedBulkTerminalEast > harbor.LocalBounds.xMax + 0.01f)
                failures.Add("bulk terminal falls outside HarborDistrict.LocalBounds");
            if (harbor.LocalBounds.xMax <= -harbor.LocalBounds.xMin + 50f)
                failures.Add("harbor plan is still a symmetric uniform strip");
        }

        static void SilosAreSkylineScale(List<string> failures)
        {
            if (HarborDistrict.BulkSiloShellTop < 65f)
                failures.Add($"bulk silos are only {HarborDistrict.BulkSiloShellTop:0.#} m high");
            if (HarborDistrict.BulkSiloElevatorTop < 90f)
                failures.Add($"bulk elevator is only {HarborDistrict.BulkSiloElevatorTop:0.#} m high");
            if (HarborDistrict.BulkSiloFootprintWidth < 55f ||
                HarborDistrict.BulkSiloFootprintDepth < 42f)
                failures.Add("bulk silo group is not a major terminal footprint");
            if (HarborDistrict.PortHeadquartersMinimumHeight < 30f)
                failures.Add("port authority headquarters is not a tall building");
        }

        static void IndustrialStripKeepsItsOriginalDeal(List<string> failures)
        {
            var plan = IndustrialLayout.ArrangeRoadside(23, out var raster);
            if (raster == null || raster.Faults != 0)
                failures.Add($"ordinary roadside seed 23 has {raster?.Faults ?? -1} road faults");
            if (plan == null || plan.Parcels.Count != 10)
                failures.Add($"ordinary roadside seed 23 dealt {plan?.Parcels.Count ?? 0} parcels, expected 10");
        }
    }
}
