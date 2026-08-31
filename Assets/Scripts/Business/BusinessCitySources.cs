using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The two shared classifiers the city providers agree on, so that no place can be
    /// claimed twice and none can fall between them.
    ///
    /// The amenity roster answers "is this harvested outdoor lot a business, and whose site
    /// is it"; the Core block roster answers the same for the harvested block prefabs. Both
    /// are named lists rather than prefixes, for PropertyDirector's stated reason: a new
    /// unit in the harvest must be a decision here, not an accident.
    /// </summary>
    public static class BusinessCitySources
    {
        /// <summary>Which provider owns an amenity unit standing in a residential plan, and
        /// what its sign says. An empty provider means the lot is not a business at all -
        /// the skatepark and the basketball court are furniture for the neighbourhood.</summary>
        public static bool AmenityBusiness(string unitName, out string providerId, out string signage)
        {
            switch (unitName)
            {
                case "caryard":
                    providerId = BusinessProviders.Compound;
                    signage = BusinessSignage.CarYard;
                    return true;
                case "gym":
                    providerId = BusinessProviders.Standalone;
                    signage = BusinessSignage.Gym;
                    return true;
                case "dinner":
                case "dinner2":
                    providerId = BusinessProviders.Standalone;
                    signage = BusinessSignage.Diner;
                    return true;
                default:
                    providerId = "";
                    signage = BusinessSignage.None;
                    return false;
            }
        }

        /// <summary>
        /// What a harvested Core block prefab is. The named three are decided; the numbered
        /// downtown blocks are UNRESOLVED - they are dense commercial city blocks with shops
        /// in them, but the harvest baked them as whole prefabs and no plan-level data says
        /// where one shop ends and the next begins. They are published as ineligible sites
        /// with that reason rather than dropped, so the audit can count what the city is
        /// still missing.
        /// </summary>
        public static bool CoreBlockBusiness(
            string sourceName, out string providerId, out string signage,
            out bool eligible, out string reason)
        {
            providerId = "";
            signage = BusinessSignage.None;
            eligible = true;
            reason = "";

            if (string.IsNullOrEmpty(sourceName))
                return false;

            switch (sourceName)
            {
                case "nightclub-block":
                    providerId = BusinessProviders.Standalone;
                    signage = BusinessSignage.Nightclub;
                    return true;
                case "warehouse-block":
                    providerId = BusinessProviders.Compound;
                    signage = BusinessSignage.Warehouse;
                    return true;
                case "police-station-block":
                    providerId = BusinessProviders.Standalone;
                    eligible = false;
                    reason = "civic: the police station is not a business.";
                    return true;
            }

            if (sourceName.StartsWith("block-"))
            {
                providerId = BusinessProviders.Standalone;
                eligible = false;
                reason = "unresolved: a harvested downtown block prefab. Its shopfronts have " +
                         "no plan-level grouping, so a business per shop cannot be published " +
                         "without a block-interior harvest.";
                return true;
            }

            // res-, park-, yard-, quay-, apron- and bank are owned by another provider or
            // are not premises at all; saying nothing here is how they stay that way.
            return false;
        }

        public static TerritoryBounds Bounds(Rect world) =>
            new TerritoryBounds(world.xMin, world.yMin, world.width, world.height);

        public static TerritoryPoint Point(Vector3 world) => new TerritoryPoint(world.x, world.z);

        public static TerritoryPoint Point(Vector2 world) => new TerritoryPoint(world.x, world.y);

        /// <summary>South, east, north, west - ResidentialLot's order, which the parking and
        /// fuel sites' entry sides share.</summary>
        public static Vector3 SideDirection(int side) => side switch
        {
            0 => Vector3.back,
            1 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.left,
        };

        /// <summary>The middle of one edge of a rectangle: where a gate or a door stands.</summary>
        public static Vector3 EdgeMidpoint(Rect local, int side) => side switch
        {
            0 => new Vector3(local.center.x, 0f, local.yMin),
            1 => new Vector3(local.xMax, 0f, local.center.y),
            2 => new Vector3(local.center.x, 0f, local.yMax),
            _ => new Vector3(local.xMin, 0f, local.center.y),
        };

        public static BusinessSiteSize SizeOf(Rect local)
        {
            var area = Mathf.Abs(local.width * local.height);
            if (area < 150f) return BusinessSiteSize.Small;
            if (area < 600f) return BusinessSiteSize.Medium;
            if (area < 2500f) return BusinessSiteSize.Large;
            return BusinessSiteSize.Compound;
        }
    }
}
