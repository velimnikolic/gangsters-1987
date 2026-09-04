using LivingCity.Outfit;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One job against one door, built the same way wherever it is ordered from. The
    /// block file used to assemble the Job by hand out of the sheet's own fields and the
    /// street card could not file one at all, which is why SMASH IT UP and TORCH IT
    /// existed in the order table and nowhere a player could reach them against a shop
    /// he was standing in front of.
    ///
    /// It reads the world - the deed, the block, the doorstep, the asking price - so a
    /// caller needs nothing but the door, the crew and how many men go. The rule about
    /// what may be ordered at all stays with <see cref="DoorOrders"/>; this asks it again
    /// at the moment of filing, because the keys were an offer made when the surface was
    /// painted and the door can have changed hands since.
    /// </summary>
    public static class DoorJobs
    {
        /// <summary>What the deed costs outright, or 0 when the door carries no price.</summary>
        public static int AskingPrice(TerritoryBusinessId id)
        {
            var business = Business.BusinessRuntime.Instance;
            return business != null && business.Populated && id.IsValid &&
                   business.Directory.TryGet(id, out var record)
                ? EconomyPrices.BuyPrice(record.Archetype)
                : 0;
        }

        /// <summary>
        /// The job, or the reason there is none. <paramref name="men"/> is what the
        /// caller has picked; the office decides afterwards whether that crew can spare
        /// them.
        /// </summary>
        public static bool TryBuild(
            TerritoryBusinessId id, OrderType type, int crewId, int men,
            out Job job, out string refusal)
        {
            job = null;
            refusal = null;
            if (!id.IsValid)
            {
                refusal = "no door picked";
                return false;
            }

            var runtime = RoadDemo.TerritoryRuntime.Instance;
            var ours = PlayerCommands.House;
            var inGoodStanding = runtime == null ||
                runtime.DoorInGoodStanding(id, ours);
            refusal = DoorOrders.Refusal(
                type, DoorHolder.Read(id), inGoodStanding);
            if (refusal != null)
                return false;

            var business = Business.BusinessRuntime.Instance;
            var shut = business != null && business.TryGetShutdown(id, out _);
            refusal = PersonClosureRefusal(type, shut);
            if (refusal != null)
            {
                return false;
            }

            var damageCause = type == OrderType.SmashUp
                ? Business.BusinessShutdownCause.SmashUp
                : type == OrderType.Torch
                    ? Business.BusinessShutdownCause.Arson
                    : type == OrderType.Bomb
                        ? Business.BusinessShutdownCause.Bomb
                        : Business.BusinessShutdownCause.None;
            if (damageCause != Business.BusinessShutdownCause.None &&
                business?.Shutdowns != null)
            {
                refusal = business.Shutdowns.DamageRefusal(
                    id, damageCause, business.CurrentGameHour);
                if (refusal != null)
                    return false;
            }

            var worth = type == OrderType.BuyPremises ? AskingPrice(id) : 0;
            if (type == OrderType.BuyPremises && worth <= 0)
            {
                refusal = "these premises carry no asking price on the book";
                return false;
            }

            var label = id.Value;
            if (runtime != null && runtime.TryGetBusinessView(id, out var view) &&
                !string.IsNullOrEmpty(view.BusinessName))
                label = view.BusinessName;

            var block = -1;
            if (runtime?.Geography != null &&
                runtime.Geography.TryGetBusinessBlock(id, out var blockId) &&
                runtime.Geography.TryGetBlock(blockId, out var definition))
                block = definition.LegacyBlockId;

            var x = 0f;
            var z = 0f;
            if (runtime != null && runtime.TryGetBusinessApproach(id, out var door))
            {
                x = door.x;
                z = door.z;
            }

            job = new Job
            {
                CrewId = crewId,
                Type = type,
                Men = Mathf.Max(1, men),
                TargetBlockId = block,
                TargetX = x,
                TargetZ = z,
                TargetLabel = label,
                TargetWorth = worth,
                TargetBusinessId = id.Value,
            };
            return true;
        }

        /// <summary>The gateway's person-order guard, split out so the headless racket
        /// contract can pin the rule without inventing a second BusinessRuntime.</summary>
        public static string PersonClosureRefusal(OrderType type, bool shut) =>
            shut && DoorOrders.IsPersonViolence(type)
                ? "nobody behind the counter"
                : null;
    }
}
