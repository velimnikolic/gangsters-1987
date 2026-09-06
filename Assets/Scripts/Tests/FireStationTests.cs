using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>Paper-side contracts for fire-station parcels and Core service coverage.</summary>
    public static class FireStationTests
    {
        const float Epsilon = 0.01f;

        public static List<string> Run()
        {
            var failures = new List<string>();
            RejectsSmallParcels(failures);
            TakesSmallestSuitableParcel(failures);
            CropsAgainstEveryRoadEdge(failures);
            Seed1987GetsCompleteServices(failures);
            return failures;
        }

        static void RejectsSmallParcels(List<string> failures)
        {
            var sites = new List<CoreAmenityLayout.Site>
            {
                Site(new Rect(0f, 0f, 45f, 35f), ParkingEntrySide.South),
                Site(new Rect(0f, 0f, 50f, 30f), ParkingEntrySide.North),
                Site(new Rect(0f, 0f, 30f, 50f), ParkingEntrySide.East),
            };
            int before = sites.Count;
            if (CoreAmenityLayout.PickFireStation(sites) != null)
                failures.Add("Fire station picker accepted a parcel smaller than 50 x 35 m");
            if (sites.Count != before)
                failures.Add("Fire station picker removed a parcel after rejecting the whole pool");
        }

        static void TakesSmallestSuitableParcel(List<string> failures)
        {
            var exact = Site(new Rect(10f, 20f, 50f, 35f), ParkingEntrySide.South);
            var large = Site(new Rect(-100f, -50f, 85f, 40f), ParkingEntrySide.North);
            var sites = new List<CoreAmenityLayout.Site> { large, exact };
            var picked = CoreAmenityLayout.PickFireStation(sites);

            if (picked == null)
            {
                failures.Add("Fire station picker found no station on known suitable parcels");
                return;
            }
            if (sites.Contains(exact) || !sites.Contains(large))
                failures.Add("Fire station did not conserve the larger development parcel");
            ExpectDimensions(picked, failures, "smallest-parcel pick");
            if (Mathf.Abs(picked.Box.yMin - exact.Box.yMin) > Epsilon)
                failures.Add("South-facing fire station was not held against its road edge");
        }

        static void CropsAgainstEveryRoadEdge(List<string> failures)
        {
            foreach (var entry in new[]
            {
                ParkingEntrySide.South, ParkingEntrySide.North,
                ParkingEntrySide.East, ParkingEntrySide.West,
            })
            {
                bool side = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
                var source = Site(
                    new Rect(10f, 20f, side ? 45f : 65f, side ? 65f : 45f), entry);
                var sites = new List<CoreAmenityLayout.Site> { source };
                var picked = CoreAmenityLayout.PickFireStation(sites);
                if (picked == null)
                {
                    failures.Add($"{entry}-facing 65 x 45 m parcel rejected the fire station");
                    continue;
                }
                ExpectDimensions(picked, failures, $"{entry}-facing crop");
                if (!Holds(source.Box, picked.Box))
                    failures.Add($"{entry}-facing fire station crop escaped its source parcel");

                bool touches = entry switch
                {
                    ParkingEntrySide.North => Near(picked.Box.yMax, source.Box.yMax),
                    ParkingEntrySide.East => Near(picked.Box.xMax, source.Box.xMax),
                    ParkingEntrySide.West => Near(picked.Box.xMin, source.Box.xMin),
                    _ => Near(picked.Box.yMin, source.Box.yMin),
                };
                if (!touches)
                    failures.Add($"{entry}-facing fire station crop does not touch its road edge");
            }
        }

        static void Seed1987GetsCompleteServices(List<string> failures)
        {
            var core = new CoreDistrict();
            try
            {
                core.Plan(Array.Empty<float>(), 1987);
                var station = core.FireStationSite;
                if (station == null)
                {
                    failures.Add("Core seed 1987 did not reserve its fire station");
                    return;
                }
                ExpectDimensions(station, failures, "Core seed 1987", fullBlock: true);
                if (core.Services.TotalPoliceCount != 3 || core.Services.FireCount != 5)
                    failures.Add("Core seed 1987 needs three total precincts and five neighbourhood fire stations");
                foreach (var housing in core.DevelopmentSites)
                    if (housing.Box.Overlaps(station.Box))
                        failures.Add("Core seed 1987 placed housing over the fire station");
                foreach (var parking in core.ParkingSites)
                    if (parking.Box.Overlaps(station.Box))
                        failures.Add("Core seed 1987 placed public parking over the fire station");
                foreach (var fuel in core.FuelSites)
                    if (fuel.Box.Overlaps(station.Box))
                        failures.Add("Core seed 1987 placed a filling station over the fire station");
            }
            finally
            {
                core.Dispose();
            }
        }

        static CoreAmenityLayout.Site Site(Rect box, ParkingEntrySide entry) =>
            new CoreAmenityLayout.Site(
                box, entry,
                Mathf.RoundToInt(box.width * box.height /
                                 (CoreLayout.Cell * CoreLayout.Cell)));

        static void ExpectDimensions(
            CoreAmenityLayout.Site site, List<string> failures, string context, bool fullBlock = false)
        {
            bool side = site.Entry == ParkingEntrySide.East ||
                        site.Entry == ParkingEntrySide.West;
            float frontage = side ? site.Box.height : site.Box.width;
            float depth = side ? site.Box.width : site.Box.height;
            float expectedFrontage = fullBlock ? FireStationBlock.BlockBounds.width : FireStationBlock.BlockFrontage;
            float expectedDepth = fullBlock ? FireStationBlock.BlockBounds.height : FireStationBlock.BlockDepth;
            if (!Near(frontage, expectedFrontage) || !Near(depth, expectedDepth))
                failures.Add($"{context} measures {frontage:F1} x {depth:F1}, not {expectedFrontage} x {expectedDepth} m");
        }

        static bool Holds(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - Epsilon && inner.xMax <= outer.xMax + Epsilon &&
            inner.yMin >= outer.yMin - Epsilon && inner.yMax <= outer.yMax + Epsilon;

        static bool Near(float one, float two) => Mathf.Abs(one - two) <= Epsilon;
    }
}
