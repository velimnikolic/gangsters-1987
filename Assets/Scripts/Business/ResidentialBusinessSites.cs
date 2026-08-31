using System.Collections.Generic;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The ground-floor commerce inside Core's residential blocks, read off
    /// <see cref="ResidentialBlockRecipe"/> and nothing else. The recipes outlive every
    /// pooled view - that is what <c>CityBlockRecycler</c> is built on - so a shop published
    /// here keeps its ID and its doorstep whether its block is on camera, released, composed
    /// in full or composed incrementally.
    ///
    /// Three kinds of place come out of one plan:
    ///   * a SHOPFRONT GROUP - a house carrying the block's shopfronts, one site per street
    ///     facade it really opens onto. A corner unit is the exception the epic names: its
    ///     glass wraps one corner and is one authored group, so it publishes one site on the
    ///     facade its own recipe seed picks (CoreResidentialFronts' rule, kept verbatim so
    ///     the outfit fronts do not move);
    ///   * a NAMED KIT STOREFRONT standing as a unit of its own - pizzapub, the radnja
    ///     shops - whose name is its sign;
    ///   * a CAFE in a gap in the row, which the lot deals explicitly.
    ///
    /// What this provider does NOT publish, by the inventory's ownership table: the car yard
    /// (a compound - BIZ-006), the gym and the diners (standalone venues - BIZ-005), and the
    /// parks, courts and skateparks, which are not businesses at all.
    /// </summary>
    public sealed class ResidentialBusinessSites : IBusinessSiteProvider
    {
        /// <summary>The face a shop spot's outfit door was always taken from. Kept as its
        /// own role so a consumer that used to walk CoreResidentialFronts gets exactly the
        /// same set, in the same order, from the catalogue.</summary>
        public const string FrontageRole = "frontage";

        /// <summary>A second shopfront round the corner of a non-corner unit: a real shop,
        /// but never an outfit's front, because the legacy picker only ever saw one door per
        /// building.</summary>
        public const string ExtraFrontageRole = "frontage-extra";

        public const string StorefrontRole = "storefront";
        public const string CafeRole = "cafe";

        readonly ResidentialBlockModel model;
        readonly DistrictFrame frame;

        public ResidentialBusinessSites(ResidentialBlockModel model, DistrictFrame frame)
        {
            this.model = model;
            this.frame = frame;
        }

        public string ProviderId => BusinessProviders.Residential;

        public IEnumerable<BusinessSite> Sites()
        {
            var sites = new List<BusinessSite>();
            if (model == null)
                return sites;

            var order = 0;
            foreach (var recipe in model.Blocks)
            {
                var plan = recipe?.Plan;
                if (plan?.Spots == null)
                    continue;

                var block = TerritoryIdentity.ExistingBlock(recipe.Id);

                for (var index = 0; index < plan.Spots.Count; index++)
                {
                    var spot = plan.Spots[index];
                    if (spot?.Unit == null)
                        continue;

                    if (spot.Unit.Kind == ResidentialKind.Storefront)
                    {
                        sites.Add(StorefrontSite(recipe, plan, spot, index, block, order++));
                        continue;
                    }

                    if (!spot.Shop)
                        continue;

                    var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                    var faces = ShopFaces(plan, spot, turn);
                    if (faces.Count == 0)
                        continue;

                    // The one face CoreResidentialFronts would have chosen, by its own salt.
                    var primary = faces[(FaceSalt(recipe, spot, index) & int.MaxValue) % faces.Count];
                    sites.Add(FrontageSite(
                        recipe, spot, index, primary, block, order++, FrontageRole));

                    // A corner unit's shopfronts wrap ONE corner: one authored group, one
                    // site. Anything else with glass on two streets is two shops.
                    if (spot.Unit.Kind == ResidentialKind.Corner)
                        continue;

                    for (var f = 0; f < faces.Count; f++)
                    {
                        if (faces[f] == primary)
                            continue;
                        sites.Add(FrontageSite(
                            recipe, spot, index, faces[f], block, order++, ExtraFrontageRole));
                    }
                }

                // The cafes the lot dealt into gaps in the row. plan.Cafe is the first of
                // them and is already in plan.Cafes; the list is the authority.
                for (var c = 0; c < plan.Cafes.Count; c++)
                    sites.Add(CafeSite(recipe, plan, plan.Cafes[c], c, block, order++));
            }

            return sites;
        }

        // ------------------------------------------------------------------ the shapes

        BusinessSite FrontageSite(
            ResidentialBlockRecipe recipe, ResidentialLot.Spot spot, int index, int side,
            TerritoryBlockId block, int order, string role)
        {
            SpotRect(recipe, spot, out var local);
            var door = DoorOnFacade(local, side);
            return new BusinessSite(
                BusinessProviders.Residential,
                recipe.Id,
                $"spot:{index}:{spot.Unit.Name}:face:{side}",
                Bounds(local),
                Point(frame.ToWorld(door)),
                Direction(side),
                BusinessSignage.None,
                SizeOf(local),
                block,
                recipe.BlockId,
                recipe.Name + " · " + spot.Unit.Name,
                role,
                order);
        }

        BusinessSite StorefrontSite(
            ResidentialBlockRecipe recipe, ResidentialLot.Plan plan, ResidentialLot.Spot spot,
            int index, TerritoryBlockId block, int order)
        {
            SpotRect(recipe, spot, out var local);
            var side = spot.AccessSide >= 0 ? spot.AccessSide
                : spot.Side >= 0 ? spot.Side
                : plan.Artery >= 0 ? plan.Artery
                : 0;
            var door = DoorOnFacade(local, side);
            return new BusinessSite(
                BusinessProviders.Residential,
                recipe.Id,
                $"spot:{index}:{spot.Unit.Name}",
                Bounds(local),
                Point(frame.ToWorld(door)),
                Direction(side),
                SignageOfUnit(spot.Unit.Name),
                BusinessSiteSize.Small,
                block,
                recipe.BlockId,
                recipe.Name + " · " + spot.Unit.Name,
                StorefrontRole,
                order);
        }

        BusinessSite CafeSite(
            ResidentialBlockRecipe recipe, ResidentialLot.Plan plan, ResidentialLot.Gap gap,
            int index, TerritoryBlockId block, int order)
        {
            GapRect(recipe, plan, gap, out var local);
            var door = DoorOnFacade(local, gap.Side);
            return new BusinessSite(
                BusinessProviders.Residential,
                recipe.Id,
                $"cafe:{index}:{ResidentialLot.SideName[gap.Side]}:{gap.At}",
                Bounds(local),
                Point(frame.ToWorld(door)),
                Direction(gap.Side),
                BusinessSignage.Cafe,
                BusinessSiteSize.Small,
                block,
                recipe.BlockId,
                recipe.Name + " · cafe",
                CafeRole,
                order);
        }

        // ------------------------------------------------------------------ the reading

        /// <summary>CoreResidentialFronts.ShopFaces, unchanged: a face counts when the block
        /// really has a street along it and the turned unit carries glass on it.</summary>
        static List<int> ShopFaces(
            ResidentialLot.Plan plan, ResidentialLot.Spot spot, ResidentialLot.Turn turn)
        {
            var result = new List<int>(2);
            Add(spot.Side);
            Add(spot.SideB);
            return result;

            void Add(int side)
            {
                if (side < 0 || side >= 4 || !plan.Street[side] ||
                    turn.Shops(side) <= 0 || result.Contains(side))
                    return;
                result.Add(side);
            }
        }

        /// <summary>The salt CoreResidentialFronts picks a corner's facade with. Copied to
        /// the digit so that migrating the outfit fronts onto this catalogue cannot move a
        /// single family's door.</summary>
        static int FaceSalt(ResidentialBlockRecipe recipe, ResidentialLot.Spot spot, int index) =>
            unchecked(recipe.Seed * 31 + spot.I * 73856093 + spot.J * 19349663 + index * 83492791);

        /// <summary>The named kit storefronts' signs. pizzapub is a pizzeria-and-pub front
        /// and reads as a pub; the radnja units are ordinary glass and roll their trade.</summary>
        static string SignageOfUnit(string unitName)
        {
            if (string.IsNullOrEmpty(unitName))
                return BusinessSignage.None;
            if (unitName.StartsWith("pizzapub"))
                return BusinessSignage.Pizza;
            return BusinessSignage.None;
        }

        static void SpotRect(
            ResidentialBlockRecipe recipe, ResidentialLot.Spot spot, out Rect local)
        {
            float cell = ResidentialLot.Cell;
            local = new Rect(
                recipe.LocalBounds.xMin + spot.I * cell,
                recipe.LocalBounds.yMin + spot.J * cell,
                spot.CW * cell,
                spot.CD * cell);
        }

        static void GapRect(
            ResidentialBlockRecipe recipe, ResidentialLot.Plan plan, ResidentialLot.Gap gap,
            out Rect local)
        {
            float cell = ResidentialLot.Cell;
            int minI = int.MaxValue, minJ = int.MaxValue, maxI = int.MinValue, maxJ = int.MinValue;
            for (var n = 0; n < Mathf.Max(1, gap.Run); n++)
            for (var k = 0; k < Mathf.Max(1, gap.Depth); k++)
            {
                var (i, j) = ResidentialLot.GapCell(plan, gap.Side, gap.At + n, k);
                minI = Mathf.Min(minI, i); maxI = Mathf.Max(maxI, i);
                minJ = Mathf.Min(minJ, j); maxJ = Mathf.Max(maxJ, j);
            }

            local = new Rect(
                recipe.LocalBounds.xMin + minI * cell,
                recipe.LocalBounds.yMin + minJ * cell,
                (maxI - minI + 1) * cell,
                (maxJ - minJ + 1) * cell);
        }

        /// <summary>The middle of one edge of a local rectangle: the doorstep on that
        /// facade. CoreResidentialFronts.DoorOnFacade, over a rectangle rather than a
        /// spot.</summary>
        static Vector3 DoorOnFacade(Rect local, int side) => side switch
        {
            0 => new Vector3(local.center.x, 0f, local.yMin),
            1 => new Vector3(local.xMax, 0f, local.center.y),
            2 => new Vector3(local.center.x, 0f, local.yMax),
            _ => new Vector3(local.xMin, 0f, local.center.y),
        };

        TerritoryPoint Direction(int side)
        {
            var local = side switch
            {
                0 => Vector3.back,
                1 => Vector3.right,
                2 => Vector3.forward,
                _ => Vector3.left,
            };
            var world = frame.ToWorldDir(local).normalized;
            return new TerritoryPoint(world.x, world.z);
        }

        TerritoryBounds Bounds(Rect local)
        {
            var world = frame.ToWorldRect(local);
            return new TerritoryBounds(world.xMin, world.yMin, world.width, world.height);
        }

        static TerritoryPoint Point(Vector3 world) => new TerritoryPoint(world.x, world.z);

        /// <summary>How big the premises read. Bands in square metres off the 5 m raster: a
        /// single shop unit is one or two cells, a whole corner house six or more.</summary>
        internal static BusinessSiteSize SizeOf(Rect local)
        {
            var area = Mathf.Abs(local.width * local.height);
            if (area < 150f) return BusinessSiteSize.Small;
            if (area < 600f) return BusinessSiteSize.Medium;
            return BusinessSiteSize.Large;
        }
    }
}
