using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// Seed-1987 regression contracts for blank Core parcels reported in the playable city.
    /// These are plan tests: they create no scene object and do not enter Play Mode, but
    /// follow the same CoreDistrict.Plan and fallback paths used by CoreDemo.
    /// </summary>
    public static class CoreVacancyTests
    {
        const int Seed = 1987;

        public static List<string> Run()
        {
            var failures = new List<string>();
            var core = new CoreDistrict();
            core.Plan(Array.Empty<float>(), Seed);

            ExactlyFiveFuelStations(core, failures);
            EveryLayoutParkingCellHasProgramme(core, failures);
            EveryDevelopmentParcelHasHousing(core, failures);
            ThinParcelsAreSolidApartmentFrontages(core, failures);
            ResidentialViewsKeepOpaqueStaticBacking(core, failures);
            EveryResidentialBlockHasFallbackGeometry(core, failures);
            InfillDoesNotRewriteTheRoadPlan(core, failures);
            DevelopmentPublishesMapAndTerritoryIdentity(core, failures);
            ProgrammesDoNotOverlap(core, failures);
            RiverAndIrregularEdgeReserveTheRightGround(core, failures);

            core.Dispose();
            return failures;
        }

        static void RiverAndIrregularEdgeReserveTheRightGround(CoreDistrict core,
            List<string> failures)
        {
            var reservations = new DistrictReservations();
            core.Reserve(reservations);
            var raster = core.Raster;
            bool sawPaved = false, sawEdge = false, sawRemoteOutside = false;
            for (int i = 0; i < raster.NX; i++)
                for (int j = 0; j < raster.NZ; j++)
                {
                    var kind = raster.At(i, j);
                    float x = raster.X(i) + CoreRoads.Cell * 0.5f;
                    float z = raster.Z(j) + CoreRoads.Cell * 0.5f;
                    if (kind == CoreRoads.Kind.Outside)
                    {
                        if (core.IsCityEdgePavement(i, j))
                        {
                            sawEdge = true;
                            if (!reservations.InPaved(x, z))
                                failures.Add("Core reservation misses the pavement at the city edge");
                        }
                        else
                        {
                            sawRemoteOutside = true;
                            if (reservations.InPaved(x, z))
                                failures.Add("Core reservation paves remote Outside ground");
                        }
                    }
                    else if (kind != CoreRoads.Kind.Water && kind != CoreRoads.Kind.Spare)
                    {
                        sawPaved = true;
                        if (!reservations.InPaved(x, z))
                            failures.Add($"Core reservation misses paved {kind} ground");
                    }
                }

            if (!sawPaved || !sawEdge || !sawRemoteOutside)
                failures.Add($"Core edge fixture incomplete: paved={sawPaved}, edge={sawEdge}, " +
                             $"remoteOutside={sawRemoteOutside}");

            var line = core.Layout.River;
            float riverX = (line.Wall + line.FarWater) * 0.5f;
            float outsideCityZ = line.Z0 - RiverBridge.Reach * 0.5f;
            if (!reservations.InWater(riverX, outsideCityZ))
                failures.Add("Core river reservation stops before reaching open water");
            if (reservations.WaterOpens.Count == 0 || reservations.WaterOpens[0])
                failures.Add("Core river reservation was published as a one-ended harbour basin");
        }

        static void ExactlyFiveFuelStations(CoreDistrict core, List<string> failures)
        {
            if (core.FuelSites.Count != 5)
                failures.Add($"Core vacancy seed {Seed}: expected 5 filling stations, got {core.FuelSites.Count}");
        }

        /// <summary>
        /// The reported holes were Parking cells inside Layout.Lots which never reached
        /// parking, fuel or development selection because a street crossed the source
        /// rectangle. Judge the raster cells themselves: every one must now belong to an
        /// explicit programme, regardless of how the source lot was split.
        /// </summary>
        static void EveryLayoutParkingCellHasProgramme(CoreDistrict core, List<string> failures)
        {
            var raster = core.Raster;
            var missed = new List<string>();
            for (int n = 0; n < core.Layout.Lots.Count; n++)
            {
                var lot = core.Layout.Lots[n];
                int i0 = Mathf.Clamp(Mathf.RoundToInt((lot.xMin - raster.X0) / CoreRoads.Cell),
                                     0, raster.NX);
                int i1 = Mathf.Clamp(Mathf.RoundToInt((lot.xMax - raster.X0) / CoreRoads.Cell),
                                     0, raster.NX);
                int j0 = Mathf.Clamp(Mathf.RoundToInt((lot.yMin - raster.Z0) / CoreRoads.Cell),
                                     0, raster.NZ);
                int j1 = Mathf.Clamp(Mathf.RoundToInt((lot.yMax - raster.Z0) / CoreRoads.Cell),
                                     0, raster.NZ);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                    {
                        if (raster.At(i, j) != CoreRoads.Kind.Parking) continue;
                        var point = new Vector2(raster.X(i) + CoreRoads.Cell * 0.5f,
                                                raster.Z(j) + CoreRoads.Cell * 0.5f);
                        if (CoreAmenityLayout.Contains(core.ParkingSites, point) ||
                            CoreAmenityLayout.Contains(core.FuelSites, point) ||
                            CoreAmenityLayout.Contains(core.DevelopmentSites, point))
                            continue;
                        if (missed.Count < 8) missed.Add($"lot {n} cell ({i},{j}) at {point}");
                    }
            }

            if (missed.Count > 0)
                failures.Add($"Core vacancy seed {Seed}: layout parking remains without a programme: " +
                             string.Join(", ", missed));
        }

        static void EveryDevelopmentParcelHasHousing(CoreDistrict core, List<string> failures)
        {
            if (core.DevelopmentSites.Count < 5)
                failures.Add($"Core vacancy seed {Seed}: only {core.DevelopmentSites.Count} former parking parcels were programmed");

            foreach (var site in core.DevelopmentSites)
            {
                var matches = RecipesAt(core, site.Box);
                if (matches.Count != 1)
                {
                    failures.Add($"Core vacancy {Where(site.Box)}: expected one residential recipe, got {matches.Count}");
                    continue;
                }

                var recipe = matches[0];
                int buildings = 0;
                foreach (var spot in recipe.Plan.Spots)
                {
                    if (spot?.Unit == null) continue;
                    if (spot.Unit.Kind == ResidentialKind.Park)
                        failures.Add($"Core vacancy {Where(site.Box)}: residential infill contains green/park filler");
                    if (!ResidentialUnits.IsLot(spot.Unit)) buildings++;
                }
                if (buildings == 0)
                    failures.Add($"Core vacancy {Where(site.Box)}: its residential recipe contains no building");
            }
        }

        static void ThinParcelsAreSolidApartmentFrontages(CoreDistrict core, List<string> failures)
        {
            int frontages = 0;
            foreach (var site in core.DevelopmentSites)
            {
                int w = Mathf.RoundToInt(site.Box.width / CoreLayout.Cell);
                int d = Mathf.RoundToInt(site.Box.height / CoreLayout.Cell);
                if (!ResidentialLot.CanFrontage(w, d, (int)site.Entry)) continue;
                frontages++;

                var matches = RecipesAt(core, site.Box);
                if (matches.Count != 1) continue;
                var plan = matches[0].Plan;
                if (!plan.Clean || plan.Spots.Count != w * d)
                    failures.Add($"Core vacancy {Where(site.Box)}: frontage has {plan.Spots.Count}/{w * d} apartment cells: " +
                                 string.Join("; ", plan.Faults));

                for (int i = 0; i < w; i++)
                    for (int j = 0; j < d; j++)
                        if (plan.Ground[i, j] != ResidentialLot.Use.Building)
                            failures.Add($"Core vacancy {Where(site.Box)}: cell ({i},{j}) is not a building");

                foreach (var spot in plan.Spots)
                    if (spot?.Unit == null || !ResidentialUnits.IsFrontage(spot.Unit))
                        failures.Add($"Core vacancy {Where(site.Box)}: frontage used a non-apartment module");
            }

            if (frontages < 5)
                failures.Add($"Core vacancy seed {Seed}: expected at least the 5 reported thin parcels, got {frontages}");
        }

        static void ResidentialViewsKeepOpaqueStaticBacking(CoreDistrict core, List<string> failures)
        {
            var raster = core.Raster;
            foreach (var site in core.DevelopmentSites)
            {
                int i0 = Mathf.RoundToInt((site.Box.xMin - raster.X0) / CoreRoads.Cell);
                int i1 = Mathf.RoundToInt((site.Box.xMax - raster.X0) / CoreRoads.Cell);
                int j0 = Mathf.RoundToInt((site.Box.yMin - raster.Z0) / CoreRoads.Cell);
                int j1 = Mathf.RoundToInt((site.Box.yMax - raster.Z0) / CoreRoads.Cell);
                bool reported = false;
                for (int i = i0; i < i1 && !reported; i++)
                    for (int j = j0; j < j1; j++)
                        if (raster.At(i, j) != CoreRoads.Kind.Parking)
                        {
                            failures.Add($"Core vacancy {Where(site.Box)}: streamed housing lost its static backing at ({i},{j}) " +
                                         $"to {raster.At(i, j)}");
                            reported = true;
                            break;
                        }
            }
        }

        static void EveryResidentialBlockHasFallbackGeometry(CoreDistrict core, List<string> failures)
        {
            var descriptions = ResidentialFallbackGeometry.Describe(core.ResidentialBlocks);
            if (descriptions.Count != core.ResidentialBlocks.Count)
                failures.Add($"Core fallback described {descriptions.Count}/{core.ResidentialBlocks.Count} residential blocks");
            if (ResidentialFallbackGeometry.GroundY <= RiverBridge.WaterY)
                failures.Add($"Core fallback ground {ResidentialFallbackGeometry.GroundY:F2} is not above water {RiverBridge.WaterY:F2}");

            var ids = new HashSet<string>();
            for (int i = 0; i < descriptions.Count; i++)
            {
                var description = descriptions[i];
                if (string.IsNullOrEmpty(description.Id) || !ids.Add(description.Id))
                {
                    failures.Add($"Core fallback has missing/duplicate id '{description.Id}'");
                    continue;
                }
                if (!core.ResidentialBlocks.TryGet(description.Id, out var recipe))
                {
                    failures.Add($"Core fallback '{description.Id}' has no residential recipe");
                    continue;
                }
                if (!Same(description.LocalBounds, recipe.LocalBounds))
                    failures.Add($"Core fallback {recipe.Name} does not cover its complete parcel");
                if (description.BuildingMasses.Count == 0)
                    failures.Add($"Core fallback {recipe.Name} has concrete but no residential building mass");

                for (int m = 0; m < description.BuildingMasses.Count; m++)
                {
                    var mass = description.BuildingMasses[m];
                    if (mass.Height < 1f || mass.LocalFootprint.width < 0.01f ||
                        mass.LocalFootprint.height < 0.01f)
                        failures.Add($"Core fallback {recipe.Name} has an empty building mass");
                    if (mass.LocalFootprint.xMin < -0.01f || mass.LocalFootprint.yMin < -0.01f ||
                        mass.LocalFootprint.xMax > recipe.LocalBounds.width + 0.01f ||
                        mass.LocalFootprint.yMax > recipe.LocalBounds.height + 0.01f)
                        failures.Add($"Core fallback {recipe.Name} has a building mass outside its parcel");
                }
            }
        }

        static void InfillDoesNotRewriteTheRoadPlan(CoreDistrict core, List<string> failures)
        {
            if (core.Raster.Faults != core.AcceptedRoadFaults)
                failures.Add($"Core vacancy seed {Seed}: residential programming changed road faults " +
                             $"from {core.AcceptedRoadFaults} to {core.Raster.Faults}");
            if (core.Raster.Report.Contains(" has no road along "))
                failures.Add($"Core vacancy seed {Seed}: infill closed a serving street");
        }

        static void DevelopmentPublishesMapAndTerritoryIdentity(CoreDistrict core, List<string> failures)
        {
            var ids = new HashSet<int>();
            foreach (var site in core.DevelopmentSites)
            {
                var matches = RecipesAt(core, site.Box);
                if (matches.Count != 1) continue;
                var recipe = matches[0];
                if (recipe.BlockId < 0)
                    failures.Add($"Core vacancy {Where(site.Box)}: map recipe has no logical block id");
                else if (!ids.Add(recipe.BlockId))
                    failures.Add($"Core vacancy {Where(site.Box)}: duplicate logical block id {recipe.BlockId}");
                if (recipe.QuarterId == CoreQuarterId.None)
                    failures.Add($"Core vacancy {Where(site.Box)}: map recipe belongs to no conquerable quarter");
                if (string.IsNullOrEmpty(recipe.Name))
                    failures.Add($"Core vacancy {Where(site.Box)}: map recipe has no block name");
            }
        }

        static void ProgrammesDoNotOverlap(CoreDistrict core, List<string> failures)
        {
            foreach (var development in core.DevelopmentSites)
            {
                foreach (var fuel in core.FuelSites)
                    if (InteriorOverlap(development.Box, fuel.Box))
                        failures.Add($"Core vacancy {Where(development.Box)} overlaps a filling station");
                foreach (var parking in core.ParkingSites)
                    if (InteriorOverlap(development.Box, parking.Box))
                        failures.Add($"Core vacancy {Where(development.Box)} overlaps retained parking");
            }
        }

        static List<ResidentialBlockRecipe> RecipesAt(CoreDistrict core, Rect box)
        {
            var found = new List<ResidentialBlockRecipe>();
            foreach (var recipe in core.ResidentialBlocks.Blocks)
                if (Same(recipe.LocalBounds, box)) found.Add(recipe);
            return found;
        }

        static bool Same(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 0.01f && Mathf.Abs(a.yMin - b.yMin) < 0.01f &&
            Mathf.Abs(a.width - b.width) < 0.01f && Mathf.Abs(a.height - b.height) < 0.01f;

        static bool InteriorOverlap(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.01f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.01f;

        static string Where(Rect box) =>
            $"{box.width:F0}x{box.height:F0} at ({box.xMin:F0},{box.yMin:F0})";
    }
}
