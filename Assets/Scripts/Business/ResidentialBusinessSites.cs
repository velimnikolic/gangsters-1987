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
    ///   * SHOP BAYS carried by a residential building - one site per doored premise on
    ///     every side, including the facade facing into the block. Doorless Shop_05 and
    ///     the display half of Shop_03 join the nearest doored 5 m bay. A genuine corner-shop
    ///     module is still one bay/business; merely standing in a corner BUILDING does not
    ///     merge all of that building's other shops into it. One legacy primary bay keeps
    ///     CoreResidentialFronts' deterministic address so existing outfit fronts do not
    ///     move;
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

        /// <summary>Every doored premise except the one legacy outfit-front candidate.
        /// These include adjacent premises and premises on the building's other sides.</summary>
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

                    // Complete amenity lots have their own provider and grouping rule:
                    // e.g. all of a diner is one venue, not one business per decorative
                    // shop mesh embedded in that authored lot.
                    if (ResidentialUnits.IsLot(spot.Unit))
                        continue;

                    var hasPhysicalBays = spot.Unit.ShopBays != null &&
                                          spot.Unit.ShopBays.Length > 0;
                    if (!spot.Shop && !hasPhysicalBays)
                        continue;

                    var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                    var streetFaces = ShopFaces(plan, spot, turn);
                    // The one face CoreResidentialFronts would have chosen, by its own
                    // salt. A building whose only physical shop faces inward never had a
                    // legacy outfit-front candidate, so it deliberately has no primary.
                    var primary = streetFaces.Count > 0
                        ? streetFaces[(FaceSalt(recipe, spot, index) & int.MaxValue) %
                                      streetFaces.Count]
                        : -1;
                    SpotRect(recipe, spot, out var whole);

                    if (hasPhysicalBays)
                    {
                        AddPhysicalBays(
                            sites, recipe, spot, index, whole, primary, block, ref order);
                        continue;
                    }

                    if (streetFaces.Count == 0)
                        continue;

                    // Read the physical model, not only the sides by which the planner put
                    // the building on the block edge. A rear/inner facade is still made of
                    // real shops and participates in the same simulation.
                    for (var side = 0; side < 4; side++)
                    {
                        turn.ShopRuns(side, shopRuns);
                        if (shopRuns.Count == 0)
                            continue;

                        var extent = side == 0 || side == 2 ? turn.CW : turn.CD;
                        var keep = 0;
                        var best = float.MaxValue;
                        for (var r = 0; r < shopRuns.Count; r++)
                        {
                            var middle = Mathf.Abs(
                                shopRuns[r].At + shopRuns[r].Len * 0.5f - extent * 0.5f);
                            if (middle >= best)
                                continue;
                            best = middle;
                            keep = r;
                        }

                        for (var r = 0; r < shopRuns.Count; r++)
                        {
                            var run = shopRuns[r];
                            var legacySiteId = r == keep
                                ? FaceSiteId(spot, index, side)
                                : FaceSiteId(spot, index, side) + ":run:" + r;
                            // The old catalogue treated one wide source mesh as one
                            // business. Keep its ID on the middle bay, then give every
                            // other equal 5 m premises a position-keyed child ID. Adding a
                            // missing bay therefore does not rename its neighbours.
                            var representative = (run.Len - 1) / 2;
                            for (var bay = 0; bay < run.Len; bay++)
                            {
                                var at = run.At + bay;
                                var local = RunRect(whole, side, (at, 1));
                                var siteId = bay == representative
                                    ? legacySiteId
                                    : legacySiteId + ":bay:" + at;
                                var role = side == primary && r == keep &&
                                           bay == representative
                                    ? FrontageRole
                                    : ExtraFrontageRole;
                                sites.Add(FrontageSite(recipe, local, side, siteId,
                                    recipe.Name + " · " + spot.Unit.Name,
                                    block, order++, role));
                            }
                        }
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

        readonly List<(int At, int Len)> shopRuns = new List<(int At, int Len)>();
        readonly List<PlacedShopBay> placedBays = new List<PlacedShopBay>();

        readonly struct PlacedShopBay
        {
            public PlacedShopBay(ResidentialShopBay source, Rect footprint, int side,
                                 Vector3 door, Vector3 outward)
            {
                Source = source;
                Footprint = footprint;
                Side = side;
                Door = door;
                Outward = outward;
            }

            public ResidentialShopBay Source { get; }
            public Rect Footprint { get; }
            public int Side { get; }
            public Vector3 Door { get; }
            public Vector3 Outward { get; }
        }

        static string FaceSiteId(ResidentialLot.Spot spot, int index, int side) =>
            $"spot:{index}:{spot.Unit.Name}:face:{side}";

        static string PhysicalSiteId(
            ResidentialLot.Spot spot, int index, ResidentialShopBay bay) =>
            $"spot:{index}:{spot.Unit.Name}:shop:{bay.Side}:" +
            $"{Mathf.RoundToInt(bay.X * 100f)}:{Mathf.RoundToInt(bay.Z * 100f)}";

        void AddPhysicalBays(
            List<BusinessSite> sites, ResidentialBlockRecipe recipe,
            ResidentialLot.Spot spot, int index, Rect whole, int primary,
            TerritoryBlockId block, ref int order)
        {
            placedBays.Clear();
            var source = spot.Unit.ShopBays;
            for (var i = 0; i < source.Length; i++)
            {
                PlaceBay(recipe, spot, whole, source[i], out var footprint, out var side,
                         out var door, out var outward);
                placedBays.Add(new PlacedShopBay(
                    source[i], footprint, side, door, outward));
            }

            // Preserve the one address/outfit candidate the old frontage provider exposed:
            // the physical bay on that face closest to the outside wall, then to its middle.
            var primaryBay = -1;
            var primaryScore = float.MaxValue;
            for (var i = 0; i < placedBays.Count; i++)
            {
                var bay = placedBays[i];
                if (bay.Side != primary || bay.Source.Door.Leaves == 0)
                    continue;
                var door = bay.Door;
                var fromOutside = bay.Side switch
                {
                    0 => Mathf.Abs(door.z - whole.yMin),
                    1 => Mathf.Abs(door.x - whole.xMax),
                    2 => Mathf.Abs(door.z - whole.yMax),
                    _ => Mathf.Abs(door.x - whole.xMin),
                };
                var fromMiddle = bay.Side == 0 || bay.Side == 2
                    ? Mathf.Abs(door.x - whole.center.x)
                    : Mathf.Abs(door.z - whole.center.y);
                var score = fromOutside * 1000f + fromMiddle;
                if (score >= primaryScore)
                    continue;
                primaryScore = score;
                primaryBay = i;
            }

            for (var i = 0; i < placedBays.Count; i++)
            {
                var bay = placedBays[i];
                if (bay.Source.Door.Leaves == 0)
                    continue;

                var footprint = bay.Footprint;
                for (var n = 0; n < placedBays.Count; n++)
                {
                    var neighbour = placedBays[n];
                    if (neighbour.Source.Door.Leaves != 0 ||
                        !JoinableDoorless(neighbour.Source) ||
                        !NearestDoorOwner(i, n))
                        continue;
                    footprint = JoinedFootprint(
                        footprint, neighbour.Footprint, bay.Side, whole);
                }

                var role = i == primaryBay ? FrontageRole : ExtraFrontageRole;
                var siteId = i == primaryBay
                    ? FaceSiteId(spot, index, primary)
                    : PhysicalSiteId(spot, index, bay.Source);
                sites.Add(FrontageSite(
                    recipe, footprint, bay.Side, siteId,
                    recipe.Name + " · " + spot.Unit.Name,
                    block, order++, role, bay.Door, bay.Outward));
            }

            bool NearestDoorOwner(int owner, int doorless)
            {
                float best = float.MaxValue;
                int nearest = -1;
                for (var candidate = 0; candidate < placedBays.Count; candidate++)
                {
                    var possible = placedBays[candidate];
                    if (possible.Source.Door.Leaves == 0)
                        continue;
                    float distance = (possible.Footprint.center -
                                      placedBays[doorless].Footprint.center).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    nearest = candidate;
                }
                return nearest == owner;
            }
        }

        static bool JoinableDoorless(ResidentialShopBay bay) =>
            StorefrontDoorCatalog.TryGet(bay.Module, out _);

        static void PlaceBay(
            ResidentialBlockRecipe recipe, ResidentialLot.Spot spot, Rect whole,
            ResidentialShopBay bay, out Rect footprint, out int side,
            out Vector3 measuredDoor, out Vector3 outward)
        {
            float cell = ResidentialLot.Cell;
            float width = spot.Unit.CW * cell;
            float depth = spot.Unit.CD * cell;
            var offset = spot.Yaw switch
            {
                90 => new Vector3(0f, 0f, width),
                180 => new Vector3(width, 0f, depth),
                270 => new Vector3(depth, 0f, 0f),
                _ => Vector3.zero,
            };
            var rotation = Quaternion.Euler(0f, spot.Yaw, 0f);
            var origin = new Vector3(
                recipe.LocalBounds.xMin + spot.I * cell, 0f,
                recipe.LocalBounds.yMin + spot.J * cell) + offset;
            var centre = origin + rotation * new Vector3(bay.X, 0f, bay.Z);
            measuredDoor = origin + rotation * new Vector3(
                bay.Door.X, 0f, bay.Door.Z);
            outward = rotation * (Quaternion.Euler(0f, bay.Door.Yaw, 0f) * Vector3.forward);
            if (bay.Door.Leaves == 0) outward = rotation * SideVector(bay.Side);
            side = SideOf(outward);

            float x = side switch
            {
                1 => centre.x - cell,
                3 => centre.x,
                _ => centre.x - cell * 0.5f,
            };
            float z = side switch
            {
                0 => centre.z,
                2 => centre.z - cell,
                _ => centre.z - cell * 0.5f,
            };
            x = Mathf.Clamp(x, whole.xMin, whole.xMax - cell);
            z = Mathf.Clamp(z, whole.yMin, whole.yMax - cell);
            if (bay.Door.Leaves > 0)
            {
                // The chamfered corner door can sit beyond the planner's rectangular
                // spot by more than a metre. Anchor the shallow site to the real wall,
                // rather than clamping that wall back inside an abstract lot rectangle.
                x = side switch
                {
                    1 => measuredDoor.x - cell,
                    3 => measuredDoor.x,
                    _ => measuredDoor.x - cell * 0.5f,
                };
                z = side switch
                {
                    0 => measuredDoor.z,
                    2 => measuredDoor.z - cell,
                    _ => measuredDoor.z - cell * 0.5f,
                };
            }
            footprint = new Rect(x, z, cell, cell);
        }

        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        static Rect JoinedFootprint(Rect a, Rect b, int side, Rect limits)
        {
            var union = Union(a, b);
            float cell = ResidentialLot.Cell;
            if (side == 0 || side == 2)
            {
                float x = Mathf.Clamp(union.center.x - cell,
                    limits.xMin, limits.xMax - cell * 2f);
                return new Rect(x, a.y, cell * 2f, cell);
            }
            float z = Mathf.Clamp(union.center.y - cell,
                limits.yMin, limits.yMax - cell * 2f);
            return new Rect(a.x, z, cell, cell * 2f);
        }

        static Vector3 SideVector(int side) => side switch
        {
            0 => Vector3.back,
            1 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.left,
        };

        static int SideOf(Vector3 outward)
        {
            if (Mathf.Abs(outward.x) > Mathf.Abs(outward.z))
                return outward.x >= 0f ? 1 : 3;
            return outward.z >= 0f ? 2 : 0;
        }

        /// <summary>One physical residential shop bay. These buildings use the same 5 m
        /// width and depth for a premises; keeping only that shallow slice also prevents
        /// shops on opposite facades from occupying the same plan footprint.</summary>
        static Rect RunRect(Rect whole, int side, (int At, int Len) run)
        {
            float cell = ResidentialLot.Cell;
            float depthX = Mathf.Min(cell, whole.width);
            float depthZ = Mathf.Min(cell, whole.height);
            return side switch
            {
                0 => new Rect(whole.xMin + run.At * cell, whole.yMin,
                              run.Len * cell, depthZ),
                1 => new Rect(whole.xMax - depthX, whole.yMin + run.At * cell,
                              depthX, run.Len * cell),
                2 => new Rect(whole.xMin + run.At * cell, whole.yMax - depthZ,
                              run.Len * cell, depthZ),
                _ => new Rect(whole.xMin, whole.yMin + run.At * cell,
                              depthX, run.Len * cell),
            };
        }

        BusinessSite FrontageSite(
            ResidentialBlockRecipe recipe, Rect local, int side, string siteId,
            string title, TerritoryBlockId block, int order, string role,
            Vector3? measuredDoor = null, Vector3? measuredOutward = null)
        {
            var door = measuredDoor ?? DoorOnFacade(local, side);
            var outward = measuredOutward ?? SideVector(side);
            // One walking stride outside the measured threshold. Keep it inside the
            // site's one-metre tolerance so a diagonal corner door remains owned by its
            // own premise rather than appearing to stand in a neighbour's footprint.
            var stand = door + outward.normalized * 0.85f;
            return new BusinessSite(
                BusinessProviders.Residential,
                recipe.Id,
                siteId,
                Bounds(local),
                Point(frame.ToWorld(stand)),
                Direction(outward),
                BusinessSignage.None,
                SizeOf(local),
                block,
                recipe.BlockId,
                title,
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
            return Direction(SideVector(side));
        }

        TerritoryPoint Direction(Vector3 local)
        {
            var world = frame.ToWorldDir(local).normalized;
            return new TerritoryPoint(world.x, world.z);
        }

        TerritoryBounds Bounds(Rect local)
        {
            var world = frame.ToWorldRect(local);
            return new TerritoryBounds(world.xMin, world.yMin, world.width, world.height);
        }

        static TerritoryPoint Point(Vector3 world) => new TerritoryPoint(world.x, world.z);

        /// <summary>How big a plan-owned premises reads from its square metres. Residential
        /// shop bays are currently 5×5 m and therefore Small; the bands remain useful for
        /// any larger plan slice published through this provider.</summary>
        internal static BusinessSiteSize SizeOf(Rect local)
        {
            var area = Mathf.Abs(local.width * local.height);
            if (area < 150f) return BusinessSiteSize.Small;
            if (area < 600f) return BusinessSiteSize.Medium;
            return BusinessSiteSize.Large;
        }
    }
}
