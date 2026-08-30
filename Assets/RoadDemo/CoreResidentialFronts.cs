using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Plan-level shopfronts in Core's streamed residential blocks. These are data, not
    /// references to composed buildings: a gang's address and the man outside it must
    /// survive when CityBlockRecycler releases an off-camera view.
    /// </summary>
    public static class CoreResidentialFronts
    {
        public readonly struct Site
        {
            public readonly string RecipeId;
            public readonly string Address;
            public readonly int BlockId;
            public readonly Vector3 Door;
            public readonly Vector3 Outward;

            public Site(string recipeId, string address, int blockId,
                        Vector3 door, Vector3 outward)
            {
                RecipeId = recipeId;
                Address = address;
                BlockId = blockId;
                Door = door;
                Outward = outward;
            }
        }

        /// <summary>
        /// One candidate per residential building that actually carries ground-floor
        /// shops. When a corner building offers shops on two street faces, its recipe
        /// seed chooses one deterministically; city/gang selection later chooses among
        /// these candidates and maximises the distance between outfits.
        /// </summary>
        public static List<Site> Collect(ResidentialBlockModel model, DistrictFrame frame)
        {
            var sites = new List<Site>();
            if (model == null)
                return sites;

            foreach (var recipe in model.Blocks)
            {
                var plan = recipe?.Plan;
                if (plan?.Spots == null)
                    continue;

                for (var index = 0; index < plan.Spots.Count; index++)
                {
                    var spot = plan.Spots[index];
                    if (spot?.Unit == null || !spot.Shop)
                        continue;

                    var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                    var faces = ShopFaces(plan, spot, turn);
                    if (faces.Count == 0)
                        continue;

                    var salt = unchecked(recipe.Seed * 31 + spot.I * 73856093 +
                                         spot.J * 19349663 + index * 83492791);
                    var side = faces[(salt & int.MaxValue) % faces.Count];
                    var localDoor = DoorOnFacade(recipe, spot, side);
                    var outward = side switch
                    {
                        0 => Vector3.back,
                        1 => Vector3.right,
                        2 => Vector3.forward,
                        _ => Vector3.left,
                    };

                    sites.Add(new Site(
                        recipe.Id,
                        recipe.Name + " · " + spot.Unit.Name,
                        recipe.BlockId,
                        frame.ToWorld(localDoor),
                        frame.ToWorldDir(outward).normalized));
                }
            }

            return sites;
        }

        static List<int> ShopFaces(ResidentialLot.Plan plan, ResidentialLot.Spot spot,
                                   ResidentialLot.Turn turn)
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

        static Vector3 DoorOnFacade(ResidentialBlockRecipe recipe,
                                    ResidentialLot.Spot spot, int side)
        {
            var cell = (float)ResidentialLot.Cell;
            var x0 = recipe.LocalBounds.xMin + spot.I * cell;
            var z0 = recipe.LocalBounds.yMin + spot.J * cell;
            var x1 = x0 + spot.CW * cell;
            var z1 = z0 + spot.CD * cell;

            return side switch
            {
                0 => new Vector3((x0 + x1) * 0.5f, 0f, z0),
                1 => new Vector3(x1, 0f, (z0 + z1) * 0.5f),
                2 => new Vector3((x0 + x1) * 0.5f, 0f, z1),
                _ => new Vector3(x0, 0f, (z0 + z1) * 0.5f),
            };
        }
    }
}
