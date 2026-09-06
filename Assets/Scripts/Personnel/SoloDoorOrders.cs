using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Personnel
{
    [Serializable]
    public sealed class SoloDoorOrder
    {
        public int characterId;
        public int parentId;
        public string businessId;
        public string blockId;
        public bool restricted;
        public int intent;
        public bool returning;
        public bool approached;
        public int health = -1;
        public bool cancelled;
        public bool hasPosition;
        public float x;
        public float z;

        public SoloDoorOrder Copy() => (SoloDoorOrder)MemberwiseClone();
    }

    /// <summary>One-person doorstep work belongs to the roster and survives its views.</summary>
    public sealed class SoloDoorOrders
    {
        readonly List<SoloDoorOrder> orders = new List<SoloDoorOrder>();
        public IReadOnlyList<SoloDoorOrder> Orders => orders;
        public int Version { get; private set; }
        public SoloDoorOrder Find(int id) => orders.Find(order => order.characterId == id);
        public static int UnitId(int characterId) => -20000000 - characterId;
        public const string BusyReason = "this man is already on a doorstep errand";

        public OpResult Begin(Roster roster, TerritoryBlockId block, TerritoryBusinessId business,
            int characterId, TerritoryRacketIntent intent, bool restricted,
            IReadOnlyList<TacticalPersonnelMapping> physical = null)
        {
            if (!business.IsValid || !block.IsValid)
                return OpResult.Fail("the doorstep is unknown");
            if (intent != TerritoryRacketIntent.Demand && intent != TerritoryRacketIntent.Threaten)
                return OpResult.Fail("one man can demand or threaten at one door");
            if (Find(characterId) != null) return OpResult.Fail(BusyReason);
            var reason = BlockMissionChoice.PersonRefusal(roster, block, characterId, restricted, physical);
            if (reason != null) return OpResult.Fail(reason);
            orders.Add(new SoloDoorOrder
            {
                characterId = characterId, parentId = BlockMissionChoice.ParentOf(roster, characterId),
                businessId = business.Value, blockId = block.Value, restricted = restricted, intent = (int)intent,
            });
            Version++;
            return OpResult.Success;
        }

        public bool Retained(Roster roster, SoloDoorOrder order)
        {
            var man = roster?.Find(order.characterId);
            if (order.cancelled || man == null || man.Gone || man.Status != CharacterStatus.Active || man.OutOfTown ||
                man.Rank != Rank.Hood || man.Specialty != Specialty.None || man.Duty != Duty.None ||
                BlockMissionChoice.ParentOf(roster, man.Id) != order.parentId) return false;
            var owner = order.restricted
                ? BlockMissionChoice.ResponsibleLeader(roster, new TerritoryBlockId(order.blockId)) : -1;
            return owner < 0 || owner == order.parentId;
        }

        public bool Keeps(Roster roster, int id)
        {
            var order = Find(id);
            return order != null && Retained(roster, order);
        }

        public int CountOf(Crew crew)
        {
            var count = 0;
            foreach (var order in orders)
                if (crew.HoodIds.Contains(order.characterId)) count++;
            return count;
        }

        public bool RecordApproach(int id)
        {
            var order = Find(id);
            if (order == null || order.approached) return false;
            order.approached = true;
            Version++;
            return true;
        }

        public void ObserveBody(int id, int health, bool dead)
        {
            var order = Find(id);
            if (order == null) return;
            order.health = dead ? 0 : Math.Max(1, health);
            order.cancelled |= dead; // a pending casualty cannot be re-created as an errand
        }

        public void Return(int id)
        {
            var order = Find(id);
            if (order == null || order.returning) return;
            order.returning = true;
            Version++;
        }

        public void Remove(int id)
        {
            if (orders.RemoveAll(order => order.characterId == id) > 0) Version++;
        }

        public SoloDoorOrder[] Snapshot()
        {
            var rows = new SoloDoorOrder[orders.Count];
            for (var i = 0; i < rows.Length; i++) rows[i] = orders[i].Copy();
            return rows;
        }

        public void Restore(SoloDoorOrder[] rows)
        {
            orders.Clear();
            foreach (var row in rows ?? Array.Empty<SoloDoorOrder>())
                if (row != null && row.characterId >= 0 && row.parentId >= 0 &&
                    new TerritoryBusinessId(row.businessId).IsValid &&
                    new TerritoryBlockId(row.blockId).IsValid && Find(row.characterId) == null &&
                    (row.intent == (int)TerritoryRacketIntent.Demand || row.intent == (int)TerritoryRacketIntent.Threaten))
                    orders.Add(row.Copy());
            Version++;
        }
    }
}
