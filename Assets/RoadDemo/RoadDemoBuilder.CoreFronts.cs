using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public partial class RoadDemoBuilder
    {
        /// <summary>
        /// Core outfits live in random generated ground-floor shops. The residential
        /// view may be pooled, so these doors are projected from the recipe onto the
        /// permanent pavement graph instead of being discovered in a live hierarchy.
        /// </summary>
        List<DemoDoor> CoreOutfitDoors()
        {
            var result = new List<DemoDoor>();
            var core = PrimaryCore;
            if (core == null)
                return result;

            var sites = CoreResidentialFronts.Collect(core.ResidentialBlocks, core.Frame);
            foreach (var site in sites)
            {
                if (!NearestFrontPavement(site.Door, out var link, out var t, out var entry))
                    continue;

                result.Add(new DemoDoor
                {
                    Pos = site.Door,
                    Outward = site.Outward,
                    BlockId = site.BlockId,
                    Address = site.Address,
                    LinkFwd = link,
                    EntryT = t,
                    EntryPos = entry,
                });
            }

            return result;
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
