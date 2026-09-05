using System.Collections.Generic;
using LivingCity.Business;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        // The physical threshold, the TurfMarks paint and the crew's doorstep share
        // one clear lane. Public for editor audits of existing, hand-arranged blocks.
        public static Rect BusinessAccessLane(Vector3 door, Vector3 outward, float reach = 4.25f)
        {
            outward.y = 0f;
            outward.Normalize();
            var right = new Vector3(outward.z, 0f, -outward.x) * 1.25f;
            var start = door - outward * .25f;
            var end = door + outward * reach;
            var a = Vector3.Min(Vector3.Min(start - right, start + right),
                                Vector3.Min(end - right, end + right));
            var b = Vector3.Max(Vector3.Max(start - right, start + right),
                                Vector3.Max(end - right, end + right));
            return Rect.MinMaxRect(a.x, a.z, b.x, b.z);
        }

        public static List<Rect> BusinessAccessLanes(ResidentialLot.Plan plan)
        {
            var lanes = new List<Rect>();
            var model = new ResidentialBlockModel();
            model.Add(new ResidentialBlockRecipe("access", "access",
                new Rect(0f, 0f, plan.W * Cell, plan.D * Cell), plan, plan.Seed));
            foreach (var site in new ResidentialBusinessSites(model, DistrictFrame.Identity).Sites())
            {
                var outward = new Vector3(site.ApproachOutward.X, 0f, site.ApproachOutward.Z).normalized;
                // Physical bays publish one stride outside the measured threshold.
                float stride = site.Role == ResidentialBusinessSites.FrontageRole ||
                    site.Role == ResidentialBusinessSites.ExtraFrontageRole ? .85f : 0f;
                var door = new Vector3(site.Approach.X, 0f, site.Approach.Z) - outward * stride;
                var doorstep = CityBusinesses.Doorstep(site);
                lanes.Add(BusinessAccessLane(door, outward,
                    Mathf.Max(4.25f, Vector3.Distance(door, doorstep) + 1f)));
            }
            foreach (var spot in plan.Spots)
            {
                if (!BusinessCitySources.AmenityBusiness(spot.Unit.Name, out _, out _)) continue;
                var bounds = new Rect(spot.I * Cell, spot.J * Cell, spot.CW * Cell, spot.CD * Cell);
                var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                for (int side = 0; side < 4; side++)
                {
                    if (turn.Doors(side) == 0) continue;
                    lanes.Add(BusinessAccessLane(BusinessCitySources.AmenityDoor(bounds, spot, side),
                        BusinessCitySources.SideDirection(side)));
                }
            }
            return lanes;
        }

        static void ReserveBusinessAccess(ResidentialLot.Plan plan) => Access.AddRange(BusinessAccessLanes(plan));

        static void ReserveMeasuredBusinessAccess(GameObject building)
        {
            foreach (var front in building.GetComponentsInChildren<Storefront>(true))
                if (front.LeafCount > 0)
                    Access.Add(BusinessAccessLane(front.DoorWorld, front.OutwardWorld));
        }
    }
}
