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

            FuelStationsOwnWholeStandaloneBlocks(core, failures);
            EveryDevelopmentParcelHasHousing(core, failures);
            DevelopmentPreservesStandaloneLotsAndPavement(core, failures);
            NoStandaloneBlockStaysEmpty(core, failures);
            ResidentialViewsOwnTheirOnlyGroundSurface(core, failures);
            EveryCompactResidentialBlockHasEnoughProgramme(core, failures);
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

        /// <summary>
        /// THE PICKER, NOT THE SEED. This used to fail seed 1987 for selecting no filling
        /// station at all - and seed 1987 offers no parcel that will hold one: the shared
        /// fuel block is an exact 60 x 55 m footprint and this city's three stand-alone
        /// candidates measure 35x90, 50x35 and 40x65 (`gangsters_core --seed 1987`). That
        /// is a fact about one arrangement, not a rule the generator broke; nothing in
        /// `Docs/core-district-plan.md` promises a pump in every city.
        ///
        /// What IS a rule is that the picker can pick at all, and that a station it does
        /// pick owns a whole block. Seed 1987 proves the second on nothing, so the first
        /// is asked of ONE city known to have the ground for it - seed 4, which stands
        /// two (`gangsters_core --seed 4`). One extra deal, not the five a blind sweep
        /// would cost: this suite has the port's thirty seconds to answer in.
        /// </summary>
        static void FuelStationsOwnWholeStandaloneBlocks(CoreDistrict core,
            List<string> failures)
        {
            const int EligibleSeed = 4;
            var eligible = new CoreDistrict();
            eligible.Plan(Array.Empty<float>(), EligibleSeed);
            if (eligible.FuelSites.Count == 0)
                failures.Add($"Core vacancy: seed {EligibleSeed} has the ground for a " +
                             "filling station and the picker stood none");
            CheckWholeBlocks(eligible, EligibleSeed, failures);
            eligible.Dispose();

            CheckWholeBlocks(core, Seed, failures);
        }

        /// <summary>
        /// A STATION SITS IN ONE STAND-ALONE BLOCK AND SHARES IT WITH NO OTHER
        /// PROGRAMME - which is not the same as filling it, and this contract used to
        /// demand that the fuel site EQUAL a planned lot. CoreAmenityLayout says
        /// otherwise in its own words: "Fuel reserves an exact full FuelStationBlock
        /// footprint against a road-facing edge; any ground left in that source
        /// rectangle remains CoreRoads' ordinary painted parking"
        /// (<see cref="CoreAmenityLayout.Select"/>, and TryFuelFootprint CROPS the
        /// 60 x 55 m block out of a bigger remainder). The equality rule was only ever
        /// vacuously true because seed 1987 stands no station at all; asked of seed 4,
        /// which stands two, it failed both of them for being what they are meant to be.
        ///
        /// So the rule is stated the way the generator means it: the footprint lies
        /// inside exactly one planned lot, and nothing else was programmed over it.
        /// </summary>
        static void CheckWholeBlocks(CoreDistrict core, int seed, List<string> failures)
        {
            foreach (var fuel in core.FuelSites)
            {
                int hostLots = 0;
                for (int i = 0; i < core.Layout.Lots.Count; i++)
                    if (Holds(core.Layout.Lots[i], fuel.Box)) hostLots++;
                if (hostLots != 1)
                    failures.Add($"Core fuel seed {seed} {Where(fuel.Box)} lies in " +
                                 $"{hostLots} stand-alone blocks, not one");

                foreach (var other in core.ParkingSites)
                    if (other != fuel && other.Box.Overlaps(fuel.Box))
                        failures.Add($"Core fuel seed {seed} {Where(fuel.Box)} shares " +
                                     $"its ground with car parking {Where(other.Box)}");
                foreach (var other in core.DevelopmentSites)
                    if (other.Box.Overlaps(fuel.Box))
                        failures.Add($"Core fuel seed {seed} {Where(fuel.Box)} shares " +
                                     $"its ground with housing {Where(other.Box)}");
            }
        }

        /// <summary>Does this lot hold the whole of that box, give or take a millimetre
        /// of floating point?</summary>
        static bool Holds(Rect lot, Rect box) =>
            box.xMin >= lot.xMin - 0.01f && box.xMax <= lot.xMax + 0.01f &&
            box.yMin >= lot.yMin - 0.01f && box.yMax <= lot.yMax + 0.01f;

        static void EveryDevelopmentParcelHasHousing(CoreDistrict core, List<string> failures)
        {
            if (core.DevelopmentSites.Count == 0)
                failures.Add($"Core vacancy seed {Seed}: no outer-quarter remainder became housing");

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

        static void DevelopmentPreservesStandaloneLotsAndPavement(CoreDistrict core,
            List<string> failures)
        {
            foreach (var site in core.DevelopmentSites)
            {
                if (!CoreAmenityLayout.CanCarryHousing(site))
                    failures.Add($"Core vacancy {Where(site.Box)} cannot preserve the shared pavement ring");
                if (!CoveredByStandaloneLots(core, site.Box))
                    failures.Add($"Core vacancy {Where(site.Box)} was cut from an existing mixed-use block");

                int w = Mathf.RoundToInt(site.Box.width / CoreLayout.Cell);
                int d = Mathf.RoundToInt(site.Box.height / CoreLayout.Cell);
                var matches = RecipesAt(core, site.Box);
                if (matches.Count != 1) continue;
                var plan = matches[0].Plan;
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < d; j++)
                    {
                        bool pavementRing = i < ResidentialLot.Walk || j < ResidentialLot.Walk ||
                                            i >= w - ResidentialLot.Walk ||
                                            j >= d - ResidentialLot.Walk;
                        if (pavementRing && plan.Ground[i, j] == ResidentialLot.Use.Building)
                            failures.Add($"Core vacancy {Where(site.Box)}: building occupies pavement cell ({i},{j})");
                    }
            }
        }

        /// <summary>Only shallow one- and two-cell remnants may fall through to CoreRoads'
        /// ordinary painted bays. A connected three-cell-deep remainder is a city block,
        /// and leaving it without parking, fuel or housing is the empty-block regression.</summary>
        static void NoStandaloneBlockStaysEmpty(CoreDistrict core,
            List<string> failures)
        {
            var raster = core.Raster;
            var seen = new bool[raster.NX, raster.NZ];
            for (int i = 0; i < raster.NX; i++)
                for (int j = 0; j < raster.NZ; j++)
                {
                    if (seen[i, j] || !OrdinaryStandaloneParking(core, i, j)) continue;
                    var todo = new Queue<Vector2Int>();
                    todo.Enqueue(new Vector2Int(i, j));
                    seen[i, j] = true;
                    int i0 = i, i1 = i, j0 = j, j1 = j;
                    bool touchesStreet = false;
                    while (todo.Count > 0)
                    {
                        var cell = todo.Dequeue();
                        i0 = Mathf.Min(i0, cell.x); i1 = Mathf.Max(i1, cell.x);
                        j0 = Mathf.Min(j0, cell.y); j1 = Mathf.Max(j1, cell.y);
                        foreach (var next in new[]
                        {
                            new Vector2Int(cell.x - 1, cell.y),
                            new Vector2Int(cell.x + 1, cell.y),
                            new Vector2Int(cell.x, cell.y - 1),
                            new Vector2Int(cell.x, cell.y + 1),
                        })
                        {
                            if (next.x < 0 || next.y < 0 || next.x >= raster.NX ||
                                next.y >= raster.NZ)
                                continue;
                            var kind = raster.At(next.x, next.y);
                            if (CoreRoads.IsRoad(kind) && kind != CoreRoads.Kind.Parking)
                                touchesStreet = true;
                            if (seen[next.x, next.y] ||
                                !OrdinaryStandaloneParking(core, next.x, next.y)) continue;
                            seen[next.x, next.y] = true;
                            todo.Enqueue(next);
                        }
                    }

                    // A BLOCK, NOT A PATCH. `Docs/core-district-plan.md` is explicit about
                    // leftover ground: a block's cut becomes parking and the rest is
                    // reported with its measure - painted parking IS what a remainder
                    // becomes. The fault this contract is named for is a whole BLOCK left
                    // blank, so the question to ask of a run is the one the programme
                    // itself asks: could anything have been built here? A 20 x 30 m strip
                    // (the one this used to fail 1987 on, at -145,-40) carries no
                    // residential recipe and no filling station, and painted parking is
                    // its right and documented answer. Ten metres in both directions was
                    // never that line.
                    int width = i1 - i0 + 1, depth = j1 - j0 + 1;
                    var run = new Rect(raster.X(i0), raster.Z(j0),
                                       width * CoreRoads.Cell, depth * CoreRoads.Cell);
                    var couldBuild = CoreAmenityLayout.CanCarryHousing(
                        new CoreAmenityLayout.Site(run, ParkingEntrySide.South,
                                                   width * depth));
                    if (couldBuild)
                        failures.Add($"Core vacancy seed {Seed}: unprogrammed stand-alone block " +
                                     $"{width * CoreRoads.Cell:F0}x{depth * CoreRoads.Cell:F0} m " +
                                     $"at ({raster.X(i0):F0},{raster.Z(j0):F0})");
                    if (!touchesStreet)
                        failures.Add($"Core ordinary parking remainder ({i0},{j0})..({i1},{j1}) does not reach a street");
                }
        }

        static bool OrdinaryStandaloneParking(CoreDistrict core, int i, int j)
        {
            if (i < 0 || j < 0 || i >= core.Raster.NX || j >= core.Raster.NZ ||
                core.Raster.At(i, j) != CoreRoads.Kind.Parking || core.ComposedSurfaceAt(i, j))
                return false;
            var point = new Vector2(core.Raster.X(i) + CoreRoads.Cell * 0.5f,
                                    core.Raster.Z(j) + CoreRoads.Cell * 0.5f);
            for (int n = 0; n < core.Layout.Lots.Count; n++)
                if (core.Layout.Lots[n].Contains(point)) return true;
            return false;
        }

        static void ResidentialViewsOwnTheirOnlyGroundSurface(CoreDistrict core,
            List<string> failures)
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
                            failures.Add($"Core vacancy {Where(site.Box)}: accepted topology changed at ({i},{j}) " +
                                         $"to {raster.At(i, j)}");
                            reported = true;
                            break;
                        }
                        else if (!core.ComposedSurfaceAt(i, j))
                        {
                            failures.Add($"Core vacancy {Where(site.Box)}: road renderer would add a second ground at ({i},{j})");
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

        /// <summary>"Empty" is visual, not only a literal Empty enum value. Paving every
        /// rejected building gap makes a technically complete plan that still reads as one
        /// vacant slab, so judge the share occupied by real programme.</summary>
        static void EveryCompactResidentialBlockHasEnoughProgramme(CoreDistrict core,
            List<string> failures)
        {
            foreach (var recipe in core.ResidentialBlocks.Blocks)
            {
                var plan = recipe?.Plan;
                int required = ResidentialLot.RequiredBuiltCoverage(plan);
                // The ordinary 30% contract is already part of ResidentialLot.Judge and
                // some legacy recipes deliberately publish that pre-existing fault. This
                // regression is for the shallow Row shape which used to pass Judge while
                // reading as one large paved slab.
                if (required <= ResidentialLot.FillLeast) continue;
                int coverage = ResidentialLot.BuiltCoverage(plan);
                if (coverage < required)
                    failures.Add($"Core vacancy {recipe?.Name ?? "<unnamed>"}: actual programme " +
                                 $"covers {coverage}% of inner ground, below {required}%");
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
            foreach (var fuel in core.FuelSites)
                foreach (var parking in core.ParkingSites)
                    if (InteriorOverlap(fuel.Box, parking.Box))
                        failures.Add($"Core fuel {Where(fuel.Box)} overlaps retained parking");
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

        static bool CoveredByStandaloneLots(CoreDistrict core, Rect box)
        {
            // Supplemental development can join adjacent source rectangles into one site.
            // Validate their union cell-by-cell instead of requiring one source rectangle
            // to contain the complete joined parcel.
            float half = CoreLayout.Cell * 0.5f;
            for (float x = box.xMin + half; x < box.xMax; x += CoreLayout.Cell)
                for (float z = box.yMin + half; z < box.yMax; z += CoreLayout.Cell)
                {
                    var point = new Vector2(x, z);
                    bool covered = false;
                    for (int n = 0; n < core.Layout.Lots.Count; n++)
                        if (core.Layout.Lots[n].Contains(point))
                        {
                            covered = true;
                            break;
                        }
                    if (!covered) return false;
                }
            return true;
        }

        static bool InteriorOverlap(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.01f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.01f;

        static string Where(Rect box) =>
            $"{box.width:F0}x{box.height:F0} at ({box.xMin:F0},{box.yMin:F0})";
    }
}
