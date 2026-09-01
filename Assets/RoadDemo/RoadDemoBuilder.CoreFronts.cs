using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public partial class RoadDemoBuilder
    {
        /// <summary>
        /// Core outfits live in ground-floor shops that ALREADY EXIST in the simulation.
        /// The residential view may be pooled, so these doors are projected from the plan
        /// onto the permanent pavement graph instead of being discovered in a live
        /// hierarchy - and since EPIC 2.5 the plan is read through the business site
        /// catalogue rather than through a second sweep of the same recipes.
        ///
        /// Only the primary shopfront of each shop building qualifies, in the order the
        /// residential provider published them: that is exactly the set and the order
        /// CoreResidentialFronts used to return, so migrating the fronts onto the
        /// catalogue cannot move a single family's address. A second shopfront round a
        /// corner is a real business but was never an outfit's door, and it stays out.
        /// </summary>
        List<DemoDoor> CoreOutfitDoors()
        {
            var result = new List<DemoDoor>();
            var core = PrimaryCore;
            if (core == null)
                return result;

            foreach (var site in FrontageSites(core))
            {
                var door = new Vector3(site.Approach.X, 0f, site.Approach.Z);
                if (!NearestFrontPavement(door, out var link, out var t, out var entry))
                    continue;

                result.Add(new DemoDoor
                {
                    Pos = door,
                    Outward = new Vector3(site.ApproachOutward.X, 0f, site.ApproachOutward.Z),
                    BlockId = site.LegacyBlockId,
                    Address = site.Label,
                    SiteId = site.SiteId,
                    LinkFwd = link,
                    EntryT = t,
                    EntryPos = entry,
                });
            }

            return result;
        }

        /// <summary>
        /// The storefront sites the residential provider published, in ITS order - the
        /// recipe order the outfit picker has always used. The catalogue itself enumerates
        /// by site id, which is the right order for the population pass and the wrong one
        /// here, so the sort is undone with the publish order the site carries.
        /// </summary>
        static List<LivingCity.Business.BusinessSite> FrontageSites(CoreDistrict core)
        {
            var sites = new List<LivingCity.Business.BusinessSite>();
            var runtime = LivingCity.Business.BusinessRuntime.Instance;

            if (runtime != null && runtime.Catalog != null)
            {
                var all = runtime.Catalog.Sites;
                for (var i = 0; i < all.Count; i++)
                    if (all[i].ProviderId == LivingCity.Business.BusinessProviders.Residential &&
                        all[i].Role == LivingCity.Business.ResidentialBusinessSites.FrontageRole)
                        sites.Add(all[i]);
            }
            else
            {
                // The business pass has not run (a scene that builds fronts before it, a
                // test harness): read the same provider directly rather than inventing a
                // second rule for the same doors.
                var provider = new LivingCity.Business.ResidentialBusinessSites(
                    core.ResidentialBlocks, core.Frame);
                foreach (var site in provider.Sites())
                    if (site.Role == LivingCity.Business.ResidentialBusinessSites.FrontageRole)
                        sites.Add(site);
            }

            sites.Sort((a, b) => a.PublishOrder.CompareTo(b.PublishOrder));
            return sites;
        }

        bool NearestFrontPavement(Vector3 door, out PedLink best, out float bestT,
                                  out Vector3 entry)
        {
            best = null;
            bestT = 0f;
            entry = default;
            var bestDistance = 18f * 18f;

            foreach (var link in _pedLinks)
            {
                if (link == null || link.Gated || link.Length < 3f)
                    continue;

                var along = link.To.Pos - link.From.Pos;
                var t = Mathf.Clamp(Vector3.Dot(door - link.From.Pos,
                    along / link.Length), 0.5f, link.Length - 0.5f);
                var point = link.From.Pos + along * (t / link.Length);
                var distance = (point - door).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = link;
                bestT = t;
                entry = point;
            }

            return best != null;
        }

        GameObject StandLogicalFront(DemoDoor door, string gangName)
        {
            if (door.Building != null)
                return door.Building;

            var owner = new GameObject("Outfit · " + gangName + " · " + door.Address);
            owner.transform.SetParent(transform, false);
            owner.transform.SetPositionAndRotation(
                door.Pos, Quaternion.LookRotation(door.Outward, Vector3.up));
            door.Building = owner;
            return owner;
        }
    }
}
