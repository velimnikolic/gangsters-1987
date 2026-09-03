using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>The flat list of places where physical crews may exchange equipment.</summary>
    public sealed class ArmorySites
    {
        readonly List<TerritoryBlockId> blocks = new List<TerritoryBlockId>();
        TerritoryBlockId headquarters;

        public IReadOnlyList<TerritoryBlockId> Blocks => blocks;
        public TerritoryBlockId Headquarters => headquarters;

        public void SetHeadquarters(TerritoryBlockId blockId)
        {
            if (!blockId.IsValid)
                return;
            var previous = headquarters;
            if (previous.IsValid && previous != blockId)
                blocks.Remove(previous);
            headquarters = blockId;
            blocks.Remove(blockId);
            blocks.Insert(0, blockId);
        }

        public bool ClearHeadquarters()
        {
            var previous = headquarters;
            if (!previous.IsValid)
                return false;
            headquarters = default;
            blocks.Remove(previous);
            return true;
        }

        public bool Add(TerritoryBlockId blockId)
        {
            if (!blockId.IsValid || blocks.Contains(blockId))
                return false;
            blocks.Add(blockId);
            return true;
        }

        public bool Remove(TerritoryBlockId blockId)
        {
            if (blockId == headquarters)
                headquarters = default;
            return blocks.Remove(blockId);
        }
        public bool Contains(TerritoryBlockId blockId) => blocks.Contains(blockId);

        public static int Distance(ITerritoryGeography geography,
            TerritoryBlockId from, TerritoryBlockId to)
        {
            if (geography == null || !from.IsValid || !to.IsValid)
                return -1;
            if (from == to)
                return 0;

            var queue = new Queue<TerritoryBlockId>();
            var distance = new Dictionary<TerritoryBlockId, int>();
            queue.Enqueue(from);
            distance[from] = 0;
            while (queue.Count > 0)
            {
                var block = queue.Dequeue();
                var nextDistance = distance[block] + 1;
                var neighbours = geography.Neighbours(block);
                for (var i = 0; i < neighbours.Count; i++)
                {
                    var next = neighbours[i];
                    if (distance.ContainsKey(next))
                        continue;
                    if (next == to)
                        return nextDistance;
                    distance[next] = nextDistance;
                    queue.Enqueue(next);
                }
            }
            return -1;
        }
    }

    public readonly struct ArmoryAccess
    {
        public readonly bool Allowed;
        public readonly bool Located;
        public readonly TerritoryBlockId BlockId;

        public ArmoryAccess(bool allowed, bool located, TerritoryBlockId blockId)
        {
            Allowed = allowed;
            Located = located;
            BlockId = blockId;
        }
    }

    public static class ArmoryGate
    {
        public static ArmoryAccess Give(Roster roster, IOrganizationPhysicalSource physical,
            ArmorySites sites, int memberId) =>
            Check(physical, sites, GroupOf(roster, memberId));

        public static ArmoryAccess Move(Roster roster, IOrganizationPhysicalSource physical,
            ArmorySites sites, int itemId, int targetLeaderId)
        {
            var source = Check(physical, sites, OwnerOf(roster, itemId));
            return source.Allowed
                ? Check(physical, sites, GroupOf(roster, targetLeaderId))
                : source;
        }

        public static ArmoryAccess Return(Roster roster, IOrganizationPhysicalSource physical,
            ArmorySites sites, int itemId) =>
            Check(physical, sites, OwnerOf(roster, itemId));

        public static ArmoryAccess GiveToFront(Roster roster,
            IOrganizationPhysicalSource physical, ArmorySites sites, int itemId) =>
            Check(physical, sites, OwnerOf(roster, itemId));

        /// <summary>No bound street source means a pure/headless host and is never gated.</summary>
        public static ArmoryAccess Check(IOrganizationPhysicalSource physical,
            ArmorySites sites, int leaderId)
        {
            if (physical == null || leaderId < 0)
                return new ArmoryAccess(true, false, default);
            if (!physical.TryLocateGroup(leaderId, out var blockId))
                return new ArmoryAccess(false, false, default);
            return new ArmoryAccess(sites != null && sites.Contains(blockId), true, blockId);
        }

        static int GroupOf(Roster roster, int id)
        {
            if (roster == null || id < 0)
                return -1;
            var crew = roster.CrewOf(id);
            if (crew != null)
                return crew.LieutenantId;
            var member = roster.Find(id);
            return member != null &&
                   (member.Rank == Rank.Boss || member.Rank == Rank.Lieutenant)
                ? member.Id : -1;
        }

        static int OwnerOf(Roster roster, int itemId)
        {
            if (roster == null)
                return -1;
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].Id == itemId)
                    return roster.Equipment[i].OwnerId >= 0
                        ? roster.Equipment[i].OwnerId : -1;
            return -1;
        }
    }
}
