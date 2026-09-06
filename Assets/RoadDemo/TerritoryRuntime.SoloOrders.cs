using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    public sealed partial class TerritoryRuntime
    {
        public SoloDoorOrders SoloOrders => Underworld.Current?.Player?.Roster?.DoorOrders;

        public string SoloRefusal(TerritoryGangId houseId, TerritoryBlockId block,
            int characterId, bool restricted)
        {
            var house = houseId.IsValid ? Underworld.Current?.Of(houseId.Value) : null;
            var roster = house?.Roster;
            if (crews == null || roster == null || house.Finished) return "the street is unavailable";
            if (roster.DoorOrders.Find(characterId) != null) return SoloDoorOrders.BusyReason;
            var physical = new List<TacticalPersonnelMapping>();
            crews.CollectPhysicalMappings(physical);
            var reason = BlockMissionChoice.PersonRefusal(roster, block, characterId, restricted, physical);
            if (reason != null) return reason;
            var crew = roster.CrewOf(characterId);
            if (crew != null)
            {
                var booked = new List<int>();
                foreach (var job in house.Runner.Book.Jobs)
                {
                    if (job.CrewId != crew.Id || !job.Live) continue;
                    LivingCity.Outfit.CrewKit.MenOnJob(roster, crew, job.Men, booked);
                    if (booked.Contains(characterId)) return "this man is already assigned to a crew job";
                }
            }
            var body = crews.BodyOf(characterId);
            if (body != null && (body.Dead || DemoCrews.PoliceStopsWork(crews.UnitOf(body)) ||
                                 body.Riding || DoorBeat.Active(body)))
                return "this man cannot leave his current duty";
            return null;
        }

        string ApproachRefusal(ApproachBusinessCommand command, out Vector3 door,
            out TerritoryBlockId block)
        {
            door = default; block = default;
            if (!command.BusinessId.IsValid || !TryGetBusinessApproach(command.BusinessId, out door))
                return "No such business in this city.";
            if (!IsRacketable(command.BusinessId)) return "That place carries no business.";
            var closure = RacketClosureRefusal(command.BusinessId);
            if (closure != null) return closure;
            if (geography != null && geography.TryGetBusinessBlock(command.BusinessId, out block))
            {
                if (command.RestrictToResponsible && command.BlockScope != block)
                    return "The door is no longer on this block.";
                var word = KeptOff(command.House, block);
                if (word != null) return word;
            }
            else if (command.RestrictToResponsible || command.GroupId.Kind == TerritoryCommandNodeKind.Character)
                return "The doorstep's block is unknown.";
            return null;
        }

        TerritoryCommandExecution ExecuteSoloApproach(ApproachBusinessCommand command,
            Vector3 door, TerritoryBlockId block)
        {
            var id = command.GroupId.Value;
            var refusal = SoloRefusal(command.House, block, id, command.RestrictToResponsible);
            if (refusal != null) return TerritoryCommandExecution.Reject(refusal);
            if (DoorTaken(command.House, SoloDoorOrders.UnitId(id), command.BusinessId, out refusal))
                return TerritoryCommandExecution.Reject(refusal);
            var house = Underworld.Current.Of(command.House.Value);
            var physical = new List<TacticalPersonnelMapping>();
            crews.CollectPhysicalMappings(physical);
            var started = house.Roster.DoorOrders.Begin(house.Roster, block, command.BusinessId, id,
                command.FollowUp, command.RestrictToResponsible, physical);
            if (!started.Ok) return TerritoryCommandExecution.Reject(started.Reason);
            var order = house.Roster.DoorOrders.Find(id);
            var unit = crews.CreateSoloUnit(house, order);
            if (unit == null || !crews.MarchTo(unit, door))
            {
                house.Roster.DoorOrders.Remove(id);
                crews.RemoveSoloUnit(unit);
                return TerritoryCommandExecution.Reject("This man could not leave headquarters.");
            }
            pendingApproaches.Add(new PendingApproach(unit.CrewId, command.BusinessId, door, command.FollowUp));
            house.Touch();
            return TerritoryCommandExecution.Pending("He is on his way; the owner is asked when he arrives.");
        }

        bool SoloMayAnswer(DemoCrews.Unit unit)
        {
            if (!unit.IsSolo) return true;
            var roster = Underworld.Current?.Of(unit.Faction)?.Roster;
            var order = unit.Boss != null ? roster?.DoorOrders.Find(unit.Boss.CharacterId) : null;
            return order != null && ReferenceEquals(order, unit.SoloOrder) && !order.returning &&
                   roster.DoorOrders.Retained(roster, order) && !unit.Wiped &&
                   !DemoCrews.PoliceStopsWork(unit);
        }

        void ReturnSolo(DemoCrews.Unit unit)
        {
            if (unit?.IsSolo == true && unit.Boss != null)
                Underworld.Current?.Of(unit.Faction)?.Roster?.DoorOrders.Return(unit.Boss.CharacterId);
        }

        void TendSoloReturn(House house, SoloDoorOrder order, DemoCrews.Unit unit)
        {
            // A headquarters hold is itself a DoorBeat visit. Finish it before
            // waiting for the shop conversation to let the man back out.
            if (CrewQuarters.Inside(unit)) { Complete(); return; }
            var billeted = CrewQuarters.Billeted(unit);
            if ((unit.TargetUnit != null && !unit.TargetUnit.Wiped) ||
                (DoorBeat.OnAVisit(unit.Boss) && !billeted))
            { unit.SoloHomeProgressAt = Time.time; return; }
            if (!crews.TrySoloHome(unit.Faction, out var home, out var business)) return;
            var gap = Vector3.Distance(unit.Position, home);
            if (unit.SoloHomeProgressAt < 0f || gap < unit.SoloHomeNearest - 1f)
            {
                unit.SoloHomeNearest = gap;
                unit.SoloHomeProgressAt = Time.time;
            }
            if (Time.time - unit.SoloHomeProgressAt >= 120f)
            {
                // The ordinary approach recovery places stranded men at their
                // destination. Only a stalled, peaceful return can take this rung.
                SetDownAtDoor(unit, home);
                Complete();
                return;
            }
            if (!billeted && (!business.IsValid || !CrewQuarters.Station(crews, unit, business)))
                CrewQuarters.Station(crews, unit, home, "HQ");

            void Complete()
            {
                house.Roster.DoorOrders.Remove(order.characterId);
                crews.RemoveSoloUnit(unit);
                house.Touch();
            }
        }

        void TendSoloOrders()
        {
            var underworld = Underworld.Current;
            if (crews == null || underworld == null) return;
            for (var h = 0; h < underworld.Count; h++)
            {
                var house = underworld.Of(h);
                var roster = house?.Roster;
                if (roster == null) continue;
                var book = roster.DoorOrders;
                for (var i = book.Orders.Count - 1; i >= 0; i--)
                {
                    var order = book.Orders[i];
                    var unit = crews.SoloUnitOf(order.characterId);
                    if (house.Finished || !book.Retained(roster, order) ||
                        crews.BodyOf(order.characterId)?.Dead == true ||
                        (unit != null && (unit.Boss?.Dead == true || DemoCrews.PoliceStopsWork(unit))))
                    {
                        DropPendingApproaches(SoloDoorOrders.UnitId(order.characterId));
                        book.Remove(order.characterId);
                        crews.RemoveSoloUnit(unit);
                        continue;
                    }
                    if (unit != null && !ReferenceEquals(unit.SoloOrder, order))
                    {
                        DropPendingApproaches(unit.CrewId);
                        crews.RemoveSoloUnit(unit);
                        unit = null;
                    }
                    if (unit != null && (unit.Boss?.Tf == null || unit.Root == null))
                    {
                        DropPendingApproaches(unit.CrewId);
                        crews.RemoveSoloUnit(unit);
                        unit = null; // removal records health before either view reference is lost
                    }
                    if (unit == null)
                    {
                        unit = crews.CreateSoloUnit(house, order);
                        if (unit == null) continue;
                        if (!order.returning)
                        {
                            var business = new TerritoryBusinessId(order.businessId);
                            if (TryGetBusinessApproach(business, out var door) &&
                                RacketClosureRefusal(business) == null &&
                                !DoorTaken(new TerritoryGangId(h), unit.CrewId, business, out _) &&
                                crews.MarchTo(unit, door))
                                pendingApproaches.Add(new PendingApproach(unit.CrewId, business, door,
                                    (TerritoryRacketIntent)order.intent));
                            else book.Return(order.characterId);
                        }
                    }
                    if (unit.Boss?.Tf == null) continue;
                    if (!order.returning && !TryGetPendingApproach(unit.CrewId, out _) &&
                        !DoorBeat.OnAVisit(unit.Boss)) book.Return(order.characterId);
                    book.ObserveBody(order.characterId, unit.Boss.Health, unit.Boss.Dead);
                    order.hasPosition = true;
                    order.x = unit.Position.x; order.z = unit.Position.z;
                    if (order.returning) TendSoloReturn(house, order, unit);

                }
            }
        }
    }
}
