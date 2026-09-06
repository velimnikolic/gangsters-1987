using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    public partial class DemoCrews
    {
        public Unit SoloUnitOf(int characterId) =>
            Units.Find(unit => unit.IsSolo && unit.CrewId == SoloDoorOrders.UnitId(characterId));

        internal Unit CreateSoloUnit(House house, SoloDoorOrder order)
        {
            var roster = house?.Roster;
            if (_root == null || roster == null || !Stands(house.GangId) ||
                !roster.DoorOrders.Retained(roster, order) ||
                !TrySoloHome(house.GangId, out var at, out _)) return null;
            var existing = SoloUnitOf(order.characterId);
            var previousBody = BodyOf(order.characterId) ?? existing?.Boss;
            if (previousBody != null)
            {
                RememberSoloBody(previousBody);
                if (previousBody.Dead || PoliceStopsWork(UnitOf(previousBody))) return null;
            }
            if (existing?.Boss?.Tf != null && existing.Root != null &&
                ReferenceEquals(existing.SoloOrder, order)) return existing;
            if (existing != null)
            {
                RemoveSoloUnit(existing);
                if (Units.Contains(existing)) return null;
            }
            var member = roster.Find(order.characterId);
            if (order.hasPosition && float.IsFinite(order.x) && float.IsFinite(order.z))
                at = new Vector3(order.x, GroundY, order.z);
            at.y = GroundY;
            _rng ??= new System.Random(roster.Seed * 7919 + 13);
            var man = BodyOf(member.Id);
            if (man != null && (man.Dead || PoliceStopsWork(UnitOf(man)))) return null;
            if (man?.Tf == null)
            {
                if (man != null) RemoveMan(member.Id);
                man = SpawnMember(member, roster, at, Quaternion.identity, HoodPace());
                if (man == null) return null;
                man.MaxHealth = HoodHealth;
                man.Health = order.health > 0 ? Mathf.Clamp(order.health, 1, HoodHealth) : HoodHealth;
                _byCharacter[member.Id] = man;
            }
            else
            {
                var was = UnitOf(man);
                if (was != null)
                {
                    was.Hoods.Remove(man);
                    if (was.Boss == man) was.Boss = null;
                }
                DoorBeat.Evict(man);
            }
            var unit = new Unit
            {
                CrewId = SoloDoorOrders.UnitId(member.Id), Faction = house.GangId,
                CommandParentId = order.parentId, IsSolo = true, SoloOrder = order,
                Name = member.FullName, GangName = LivingCity.Gangs.GangCatalog.Names[house.GangId],
                Boss = man, Root = new GameObject("Errand · " + member.FullName).transform,
            };
            unit.Root.SetParent(_root, false);
            man.Tf.SetParent(unit.Root, true);
            man.DisplayName = member.FullName;
            man.IsLieutenant = false;
            man.Faction = house.GangId;
            man.CrowdGroupId = unit.CrowdGroupId;
            man.RoamsAlone = false;
            ArmFromLedger(roster, man);
            Units.Add(unit);
            return unit;
        }

        void RememberSoloBody(CrewWalker man)
        {
            if (man == null) return;
            var book = LivingCity.Outfit.Underworld.Current?.Of(man.Faction)?.Roster?.DoorOrders;
            var unit = SoloUnitOf(man.CharacterId);
            if (unit != null && !ReferenceEquals(unit.SoloOrder, book?.Find(man.CharacterId))) return;
            book?.ObserveBody(man.CharacterId, man.Health, man.Dead);
        }

        internal bool TrySoloHome(int faction, out Vector3 home,
            out LivingCity.Territory.TerritoryBusinessId business)
        {
            var front = FrontOf(faction);
            business = front != null ? front.BusinessId : default;
            home = front != null ? front.Outside : _outfitAnchor;
            if (front != null) return true;
            if (faction != LivingCity.Gameplay.PlayerCommands.House.Value) return false;
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            if (outfit != null && outfit.TryGetHeadquarters(out var headquarters, out _)) home = headquarters;
            return true;
        }

        internal void RemoveSoloUnit(Unit unit)
        {
            if (unit == null || !unit.IsSolo) return;
            // Death and custody keep the actual body for their own lifecycle.
            if (unit.Boss?.Dead == true || PoliceStopsWork(unit))
            { _seenVersion = int.MinValue; return; }
            var man = unit.Boss;
            CrewQuarters.Forget(unit);
            if (man != null && UnitOf(man) == unit) RemoveMan(man.CharacterId);
            Units.Remove(unit);
            if (Selected == unit) Selected = null;
            if (unit.Root != null) Destroy(unit.Root.gameObject);
            _seenVersion = int.MinValue;
        }

        void KeepSoloUnits(Underworld underworld, List<Unit> liveUnits)
        {
            foreach (var unit in Units)
            {
                if (!unit.IsSolo || unit.Boss == null || unit.Boss.Dead || PoliceStopsWork(unit)) continue;
                var roster = underworld.Of(unit.Faction)?.Roster;
                if (roster == null || !roster.DoorOrders.Keeps(roster, unit.Boss.CharacterId)) continue;
                liveUnits.Add(unit);
                ArmFromLedger(roster, unit.Boss);
            }
        }

        bool IsSoloReserved(Underworld underworld, int characterId, CrewWalker body) =>
            body != null && underworld.Of(body.Faction)?.Roster is Roster roster &&
            roster.DoorOrders.Keeps(roster, characterId);
    }
}
