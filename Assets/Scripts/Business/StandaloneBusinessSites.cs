using System.Collections.Generic;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The standalone commercial, hospitality and leisure venues Core's plan actually
    /// places: the nightclub block, the gyms and diners standing as complete lots inside
    /// residential plans, and the promenade's terraces, diner and fairground.
    ///
    /// Each venue's physical purpose supplies a FIXED sign - a nightclub block can never be
    /// rolled into a bakery - and each publishes exactly one site. The provider reads what
    /// the plan placed; it never re-rolls the placement, and the landmark rules that decided
    /// it (one casino per city, the gun shop's unique-buildings ceiling) stay where they are.
    ///
    /// The quay's rooms are re-derived the way TurfMapSurvey derives them: same seed, same
    /// dice, same QuayWalk.ForQuay call, no GameObject touched.
    /// </summary>
    public sealed class StandaloneBusinessSites : IBusinessSiteProvider
    {
        public const string VenueRole = "venue";
        public const string QuayRole = "quay";
        public const string CoreBlockRole = "core-block";

        readonly CoreDistrict core;

        public StandaloneBusinessSites(CoreDistrict core) => this.core = core;

        public string ProviderId => BusinessProviders.Standalone;

        public IEnumerable<BusinessSite> Sites()
        {
            var sites = new List<BusinessSite>();
            if (core == null)
                return sites;

            var order = 0;
            CollectAmenities(sites, ref order);
            CollectCoreBlocks(sites, ref order);
            CollectQuayRooms(sites, ref order);
            return sites;
        }

        // ------------------------------------------------------- lots inside a block

        void CollectAmenities(List<BusinessSite> sites, ref int order)
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
                        provider != BusinessProviders.Standalone)
                        continue;

                    float cell = ResidentialLot.Cell;
                    var local = new Rect(
                        recipe.LocalBounds.xMin + spot.I * cell,
                        recipe.LocalBounds.yMin + spot.J * cell,
                        spot.CW * cell, spot.CD * cell);
                    var side = BusinessCitySources.AmenityApproachSide(plan, spot);

                    sites.Add(new BusinessSite(
                        BusinessProviders.Standalone,
                        recipe.Id,
                        $"lot:{index}:{spot.Unit.Name}",
                        BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                        BusinessCitySources.Point(
                            core.Frame.ToWorld(BusinessCitySources.AmenityDoor(local, spot, side))),
                        BusinessCitySources.Point(
                            core.Frame.ToWorldDir(BusinessCitySources.SideDirection(side)).normalized),
                        signage,
                        BusinessCitySources.SizeOf(local),
                        TerritoryIdentity.ExistingBlock(recipe.Id),
                        recipe.BlockId,
                        recipe.Name + " · " + spot.Unit.Name,
                        VenueRole,
                        order++));
                }
            }
        }

        // ------------------------------------------------------- harvested blocks

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
                    provider != BusinessProviders.Standalone)
                    continue;

                var local = block.LocalBounds;
                sites.Add(new BusinessSite(
                    BusinessProviders.Standalone,
                    block.StableId,
                    "block:" + block.SourceName,
                    BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                    BusinessCitySources.Point(
                        core.Frame.ToWorld(BusinessCitySources.EdgeMidpoint(local, 0))),
                    BusinessCitySources.Point(
                        core.Frame.ToWorldDir(BusinessCitySources.SideDirection(0)).normalized),
                    signage,
                    BusinessCitySources.SizeOf(local),
                    TerritoryIdentity.ExistingBlock(block.StableId),
                    block.Id,
                    block.Name,
                    CoreBlockRole,
                    order++,
                    eligible,
                    reason));
            }
        }

        // ------------------------------------------------------- the promenade

        /// <summary>
        /// The quay's rooms, re-dealt from the plan rather than read off the composed strip.
        /// The dice below are CoreDistrict.StandQuays' own - seed, then the stretch's corner
        /// - so this pass and the built promenade always agree about which stretch got the
        /// fairground.
        /// </summary>
        void CollectQuayRooms(List<BusinessSite> sites, ref int order)
        {
            var layout = core.Layout;
            if (layout == null || layout.Quays.Count == 0)
                return;

            var wants = QuayWalk.Cast(layout);
            for (var q = 0; q < layout.Quays.Count; q++)
            {
                var block = layout.Quays[q];
                var box = block.Box;
                var dice = unchecked(core.LayoutSeed * 7919 +
                    Mathf.RoundToInt(box.xMin) * 104729 +
                    Mathf.RoundToInt(box.yMin) * 1299709);
                var walk = QuayWalk.ForQuay(layout, block, wants[q], new System.Random(dice));

                for (var r = 0; r < walk.Rooms.Count; r++)
                {
                    var room = walk.Rooms[r];
                    string signage;
                    switch (room.Programme)
                    {
                        case QuayWalk.Programme.Terrace: signage = BusinessSignage.Cafe; break;
                        case QuayWalk.Programme.Diner: signage = BusinessSignage.Diner; break;
                        case QuayWalk.Programme.Fair: signage = BusinessSignage.Fairground; break;
                        default: continue; // a lawn, a fountain, a landing: no premises
                    }

                    var z0 = layout.River.East
                        ? box.yMin + room.Z0 * QuayWalk.Cell
                        : box.yMax - room.Z1 * QuayWalk.Cell;
                    var local = new Rect(
                        box.xMin, z0, box.width, room.Length * QuayWalk.Cell);

                    // The approach is the middle of the room: the promenade is walked from
                    // both ends and the plan does not name a door on one edge of it.
                    sites.Add(new BusinessSite(
                        BusinessProviders.Standalone,
                        block.StableId ?? ("quay:" + q),
                        $"room:{r}:{room.Programme}",
                        BusinessCitySources.Bounds(core.Frame.ToWorldRect(local)),
                        BusinessCitySources.Point(core.Frame.ToWorld(
                            new Vector3(local.center.x, 0f, local.center.y))),
                        default,
                        signage,
                        BusinessCitySources.SizeOf(local),
                        TerritoryIdentity.ExistingBlock(block.StableId ?? ""),
                        block.BlockId,
                        block.Label + " · " + room.Programme,
                        QuayRole,
                        order++));
                }
            }
        }
    }
}
