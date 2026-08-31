using System.Collections.Generic;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The businesses whose physical extent is a compound rather than a door: the car yards,
    /// the filling stations, the warehouse compound, and the harbour.
    ///
    /// The rule this provider exists to keep is the one the epic states in capitals: never a
    /// business per renderer, hall, tank, pump, silo or recycled view. A car yard with an
    /// office and rows of stock is ONE site; a harbour with sheds, gantries, a tank farm and
    /// a gate is ONE firm. The grouping key is explicit - the plan's own compound id - the
    /// footprint is the union of the compound's ground, and the approach is the public gate
    /// the plan already knows about, because a crew has to be able to walk up to it later.
    ///
    /// Sources that exist in this project but not in the Core city plan - the industrial
    /// quarter's factories and refineries, which only IndustrialDemo and HarborDemo stand -
    /// are recorded in Docs/business-inventory.md as unsupported rather than guessed at here.
    /// </summary>
    public sealed class CompoundBusinessSites : IBusinessSiteProvider
    {
        public const string YardRole = "yard";
        public const string FuelRole = "fuel";
        public const string CoreBlockRole = "core-block";
        public const string PortRole = "port";

        readonly CoreDistrict core;
        readonly IReadOnlyList<IDistrict> districts;

        public CompoundBusinessSites(CoreDistrict core, IReadOnlyList<IDistrict> districts)
        {
            this.core = core;
            this.districts = districts;
        }

        public string ProviderId => BusinessProviders.Compound;

        public IEnumerable<BusinessSite> Sites()
        {
            var sites = new List<BusinessSite>();
            var order = 0;
            if (core != null)
            {
                CollectYards(sites, ref order);
                CollectFuelStations(sites, ref order);
                CollectCoreBlocks(sites, ref order);
            }

            CollectHarbours(sites, ref order);
            return sites;
        }

        // ------------------------------------------------------------------ car yards

        void CollectYards(List<BusinessSite> sites, ref int order)
        {
            var model = core.ResidentialBlocks;
            if (model == null)
                return;

            foreach (var recipe in model.Blocks)
            {
                var plan = recipe?.Plan;
                if (plan?.Spots == null)
                    continue;

                for (var index = 0; index < plan.Spots.Count; index++)
                {
                    var spot = plan.Spots[index];
                    if (spot?.Unit == null || spot.Unit.Kind != ResidentialKind.Amenity)
                        continue;
                    if (!BusinessCitySources.AmenityBusiness(
                            spot.Unit.Name, out var provider, out var signage) ||
                        provider != BusinessProviders.Compound)
                        continue;

                    float cell = ResidentialLot.Cell;
                    var local = new Rect(
                        recipe.LocalBounds.xMin + spot.I * cell,
                        recipe.LocalBounds.yMin + spot.J * cell,
                        spot.CW * cell, spot.CD * cell);
                    var side = spot.AccessSide >= 0 ? spot.AccessSide
                        : spot.Side >= 0 ? spot.Side
                        : plan.Artery >= 0 ? plan.Artery : 0;

                    sites.Add(new BusinessSite(
                        BusinessProviders.Compound,
                        recipe.Id,
                        $"lot:{index}:{spot.Unit.Name}",
                        BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                        BusinessCitySources.Point(
                            core.Frame.ToWorld(BusinessCitySources.EdgeMidpoint(local, side))),
                        BusinessCitySources.Point(
                            core.Frame.ToWorldDir(BusinessCitySources.SideDirection(side)).normalized),
                        signage,
                        BusinessSiteSize.Compound,
                        TerritoryIdentity.ExistingBlock(recipe.Id),
                        recipe.BlockId,
                        recipe.Name + " · " + spot.Unit.Name,
                        YardRole,
                        order++));
                }
            }
        }

        // ------------------------------------------------------------- filling stations

        /// <summary>
        /// A filling station owns its whole block (CoreDistrict's rule: it never shares one),
        /// so the parcel IS the compound. The forecourt is entered from the site's own entry
        /// side, which is the same side the block's pumps are laid out from.
        /// </summary>
        void CollectFuelStations(List<BusinessSite> sites, ref int order)
        {
            var fuel = core.FuelSites;
            if (fuel == null)
                return;

            for (var i = 0; i < fuel.Count; i++)
            {
                var site = fuel[i];
                var local = site.Box;
                var side = (int)site.Entry;
                var key = $"{Mathf.RoundToInt(local.xMin)}:{Mathf.RoundToInt(local.yMin)}";

                sites.Add(new BusinessSite(
                    BusinessProviders.Compound,
                    "core:" + core.LayoutSeed + ":fuel",
                    "station:" + key,
                    BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                    BusinessCitySources.Point(
                        core.Frame.ToWorld(BusinessCitySources.EdgeMidpoint(local, side))),
                    BusinessCitySources.Point(
                        core.Frame.ToWorldDir(BusinessCitySources.SideDirection(side)).normalized),
                    BusinessSignage.Fuel,
                    BusinessSiteSize.Compound,
                    BlockAt(local.center),
                    BlockIdAt(local.center),
                    "Filling station " + key,
                    FuelRole,
                    order++));
            }
        }

        // ------------------------------------------------------------- warehouse blocks

        void CollectCoreBlocks(List<BusinessSite> sites, ref int order)
        {
            var territory = core.Territory;
            if (territory == null)
                return;

            foreach (var block in territory.Blocks)
            {
                if (!BusinessCitySources.CoreBlockBusiness(
                        block.SourceName, out var provider, out var signage,
                        out var eligible, out var reason) ||
                    provider != BusinessProviders.Compound)
                    continue;

                var local = block.LocalBounds;
                sites.Add(new BusinessSite(
                    BusinessProviders.Compound,
                    block.StableId,
                    "block:" + block.SourceName,
                    BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                    BusinessCitySources.Point(
                        core.Frame.ToWorld(BusinessCitySources.EdgeMidpoint(local, 0))),
                    BusinessCitySources.Point(
                        core.Frame.ToWorldDir(BusinessCitySources.SideDirection(0)).normalized),
                    signage,
                    BusinessSiteSize.Compound,
                    TerritoryIdentity.ExistingBlock(block.StableId),
                    block.Id,
                    block.Name,
                    CoreBlockRole,
                    order++,
                    eligible,
                    reason));
            }
        }

        // ------------------------------------------------------------------ the harbour

        /// <summary>
        /// One firm for the whole waterfront. PropertyDirector's judgement, kept: clicking
        /// any shed inside the wall means "the port", not "shed #4", so the sheds, the
        /// gantries, the tank farm and the gate are one business with one gate.
        /// </summary>
        void CollectHarbours(List<BusinessSite> sites, ref int order)
        {
            if (districts == null)
                return;

            for (var i = 0; i < districts.Count; i++)
            {
                if (!(districts[i] is HarborDemo.HarborDistrict harbor))
                    continue;

                var local = harbor.LocalBounds;
                var world = harbor.Frame.ToWorldRect(local);
                var gate = harbor.Portals != null && harbor.Portals.Count > 0
                    ? harbor.Frame.ToWorld(harbor.Portals[0].Local)
                    : new Vector3(world.center.x, 0f, world.center.y);
                var outward = harbor.Portals != null && harbor.Portals.Count > 0
                    ? harbor.Frame.ToWorldDir(harbor.Portals[0].LocalDir).normalized
                    : Vector3.zero;

                sites.Add(new BusinessSite(
                    BusinessProviders.Compound,
                    "harbor:" + Mathf.RoundToInt(world.xMin) + ":" +
                        Mathf.RoundToInt(world.yMin),
                    "company",
                    BusinessCitySources.Bounds(world),
                    BusinessCitySources.Point(gate),
                    BusinessCitySources.Point(outward),
                    BusinessSignage.Port,
                    BusinessSiteSize.Compound,
                    default,
                    -1,
                    harbor.Name,
                    PortRole,
                    order++));
            }
        }

        // ------------------------------------------------------------------ block hints

        TerritoryBlockId BlockAt(Vector2 local)
        {
            var block = core.Territory?.BlockAt(local);
            return block != null ? TerritoryIdentity.ExistingBlock(block.StableId) : default;
        }

        int BlockIdAt(Vector2 local) => core.Territory?.BlockAt(local)?.Id ?? -1;
    }
}
