using LivingCity.Territory;
using RoadDemo;
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

        /// <summary>
        /// Which face of an amenity is connected to the public pavement. Mixed-block
        /// amenities are placed as lots, so unlike residential units they may have no
        /// authored AccessSide. Falling straight back to the block artery can point a
        /// business door through another programme - the seed-1987 gym's east face is
        /// behind a cafe terrace. Prefer a real door whose centre has an unobstructed
        /// plan-cell corridor to a street; only fall back to the old frontage answer
        /// when the plan offers no such face.
        /// </summary>
        public static int AmenityApproachSide(
            ResidentialLot.Plan plan, ResidentialLot.Spot spot)
        {
            if (plan == null || spot?.Unit == null)
                return 0;
            if (spot.AccessSide >= 0 && spot.AccessSide < 4)
                return spot.AccessSide;

            var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
            var best = -1;
            var bestDistance = int.MaxValue;
            for (var side = 0; side < 4; side++)
            {
                if (!plan.Street[side] || !turn.Face(side) ||
                    turn.Doors(side) + turn.Shops(side) <= 0 ||
                    !OpenToStreet(plan, spot, side, out var distance))
                    continue;

                if (best < 0 || distance < bestDistance ||
                    (distance == bestDistance && side == plan.Artery))
                {
                    best = side;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
                return best;
            if (spot.Side >= 0 && spot.Side < 4)
                return spot.Side;
            return plan.Artery >= 0 && plan.Artery < 4 ? plan.Artery : 0;
        }

        static bool OpenToStreet(
            ResidentialLot.Plan plan, ResidentialLot.Spot spot,
            int side, out int distance)
        {
            distance = 0;
            if (plan.Ground == null || plan.W <= 0 || plan.D <= 0)
                return false;

            var along = side == 0 || side == 2
                ? spot.I + (spot.CW - 1) / 2
                : spot.J + (spot.CD - 1) / 2;
            if (spot.Unit.Name == "gym")
            {
                float cell = ResidentialLot.Cell;
                var opening = AmenityDoor(new Rect(spot.I * cell, spot.J * cell,
                    spot.CW * cell, spot.CD * cell), spot, side);
                along = Mathf.FloorToInt((side == 0 || side == 2 ? opening.x : opening.z) / cell);
            }
            var i = side switch
            {
                1 => spot.I + spot.CW,
                3 => spot.I - 1,
                _ => along,
            };
            var j = side switch
            {
                0 => spot.J - 1,
                2 => spot.J + spot.CD,
                _ => along,
            };

            while (i >= 0 && j >= 0 && i < plan.W && j < plan.D)
            {
                var use = plan.Ground[i, j];
                if (use == ResidentialLot.Use.Walkway)
                    return true;
                if (use == ResidentialLot.Use.Empty ||
                    use == ResidentialLot.Use.Building ||
                    use == ResidentialLot.Use.Yard ||
                    use == ResidentialLot.Use.Cafe ||
                    use == ResidentialLot.Use.Park ||
                    use == ResidentialLot.Use.Subway)
                    return false;

                distance++;
                i += ResidentialLot.Step[side, 0];
                j += ResidentialLot.Step[side, 1];
            }

            return false;
        }

        /// <summary>The venue's real opening, in the same frame as its footprint.</summary>
        public static Vector3 AmenityDoor(Rect local, ResidentialLot.Spot spot, int side)
        {
            if (spot.Unit.Name != "gym") return EdgeMidpoint(local, side);
            int sourceSide = (side + spot.Yaw / 90) % 4;
            if (sourceSide != 0 && sourceSide != 3) return EdgeMidpoint(local, side);
            float size = ResidentialGym.Cells * ResidentialLot.Cell;
            var point = sourceSide == 0 ? new Vector3(size * .5f, 0f, 0f)
                : new Vector3(0f, 0f, ResidentialGym.RampCentreZ);
            var offset = spot.Yaw switch
            {
                90 => new Vector3(0f, 0f, size),
                180 => new Vector3(size, 0f, size),
                270 => new Vector3(size, 0f, 0f),
                _ => Vector3.zero,
            };
            return new Vector3(local.xMin, 0f, local.yMin) + offset +
                Quaternion.Euler(0f, spot.Yaw, 0f) * point;
        }

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
