using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;
using LivingCity.UI;

namespace LivingCity.Tests
{
    public static class SoloDoorOrdersTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            void Check(bool value, string reason) { if (!value) failures.Add("SOLO: " + reason); }
            var roster = new Roster();
            Character Add(string name, Rank rank)
            {
                var man = new Character { Id = roster.NextCharacterId(), FirstName = name,
                    Surname = "Test", Rank = rank, Status = CharacterStatus.Active };
                roster.Members.Add(man); return man;
            }
            var boss = Add("Don", Rank.Boss);
            roster.Organization.BossId = boss.Id;
            var leader = Add("Lou", Rank.Lieutenant);
            var other = Add("Declan", Rank.Lieutenant);
            var own = new Crew { Id = roster.NextCrewId(), LieutenantId = leader.Id };
            var theirs = new Crew { Id = roster.NextCrewId(), LieutenantId = other.Id };
            roster.Crews.Add(own); roster.Crews.Add(theirs);
            var hoods = new List<Character>();
            for (var i = 0; i < 7; i++)
            { var man = Add("Man" + i, Rank.Hood); own.HoodIds.Add(man.Id); hoods.Add(man); }
            var outsider = Add("Other", Rank.Hood); theirs.HoodIds.Add(outsider.Id);
            var free = Add("Free", Rank.Hood); roster.Organization.BossHoodIds.Add(free.Id);
            var block = new TerritoryBlockId("solo:own");
            var door = new TerritoryBusinessId("solo:door");
            roster.Organization.BlockResponsibilities.Add(new OrganizationBlockResponsibility(block, leader.Id));
            var crews = new List<Crew>(); var reserve = new List<Character>();
            BlockMissionChoice.Collect(roster, block, true, crews, reserve);
            Check(crews.Count == 1 && crews[0] == own && reserve.Count == 3,
                "the responsible crew and its three reserve men must appear separately");
            for (var i = 0; i < 4; i++) Check(!reserve.Contains(hoods[i]), "a street-line man appeared individually");
            Check(BlockMissionChoice.BagRefusal(roster, block, outsider.Id) != null &&
                  BlockMissionChoice.BagRefusal(roster, block, boss.Id) != null &&
                  BlockMissionChoice.BagRefusal(roster, block, hoods[4].Id) == null,
                "the bag menu admitted a passer-by or refused its own hood");
            Check(BlockMissionChoice.BagRefusal(roster, new TerritoryBlockId("solo:none"), hoods[4].Id) != null,
                "a standing crew became an unassigned block's bag authority");

            var physical = new List<TacticalPersonnelMapping>
            {
                new TacticalPersonnelMapping(own.Id * 2, leader.Id,
                    new[] { leader.Id, hoods[1].Id, hoods[2].Id, hoods[3].Id, hoods[4].Id }),
            };
            BlockMissionChoice.Collect(roster, block, true, crews, reserve, physical);
            Check(reserve.Contains(hoods[0]) && !reserve.Contains(hoods[4]),
                "the picker ignored the actual physical line");
            Check(!roster.DoorOrders.Begin(roster, block, door, hoods[4].Id,
                TerritoryRacketIntent.Demand, true, physical).Ok, "a live crew member was sent alone");
            Check(!roster.DoorOrders.Begin(roster, block, door, outsider.Id,
                TerritoryRacketIntent.Demand, true).Ok, "another branch supplied a solo man");
            Check(!roster.DoorOrders.Begin(roster, block, door, free.Id,
                TerritoryRacketIntent.Demand, true).Ok, "a lieutenant borrowed the Don's reserve");
            var manId = hoods[5].Id;
            Check(roster.DoorOrders.Begin(roster, block, door, manId,
                TerritoryRacketIntent.Demand, true).Ok, "a reserve hood could not demand alone");
            Check(roster.DoorOrders.Orders.Count == 1 && own.HoodIds.Contains(manId) &&
                  own.LieutenantId == leader.Id, "sending a hood rewrote the organization");
            Check(!roster.DoorOrders.Begin(roster, block, door, manId,
                TerritoryRacketIntent.Demand, true).Ok, "the same man got two concurrent errands");
            Check(!RosterOps.SetDuty(roster, manId, Duty.Collector).Ok,
                "a man out on an errand was appointed collector");
            var candidates = new List<Character>();
            CollectorChoice.Candidates(roster, own, candidates);
            Check(!candidates.Contains(hoods[5]), "automatic collector selection took a man on an errand");
            hoods[5].SetHalfSteps(CharacterAttribute.Persuasion, 10);
            var job = new Outfit.Job { CrewId = own.Id, Type = Outfit.OrderType.Guard, Men = 99 };
            job.BlockTargets.Add(1);
            Check(new Outfit.CampaignRunner().Issue(roster, job).Ok && job.Men == 7,
                "a reserve man's errand froze the crew or overstated available headcount");
            var working = new List<int>();
            Outfit.CrewKit.MenOnJob(roster, own, job.Men, working);
            Check(working.Count == 7 && !working.Contains(manId), "the job borrowed the absent man's labour");
            Check(Outfit.CrewKit.BestAt(roster, own, CharacterAttribute.Persuasion) < 10,
                "the job borrowed the absent man's skill");
            Check(roster.DoorOrders.RecordApproach(manId) && !roster.DoorOrders.RecordApproach(manId),
                "one errand recorded two arrivals");

            roster.DoorOrders.ObserveBody(manId, 1, false);
            var saved = RosterSnapshot.Snapshot(roster);
            var restored = new Roster(); RosterSnapshot.Restore(restored, saved);
            var trip = restored.DoorOrders.Find(manId);
            Check(trip != null && !trip.returning && trip.businessId == door.Value &&
                  restored.DoorOrders.Keeps(restored, manId), "an outgoing demand did not survive save/load");
            Check(trip.health == 1, "view replacement or load healed the wounded errand runner");
            Check(trip.approached && !restored.DoorOrders.RecordApproach(manId) && !trip.returning,
                "a mid-visit load repeated the approach or skipped the unanswered demand");
            restored.DoorOrders.Return(manId);
            Check(!saved.doorOrders[0].returning && !roster.DoorOrders.Find(manId).returning,
                "a restored mission still aliases its snapshot or original");
            var answered = RosterSnapshot.Snapshot(restored);
            RosterSnapshot.Restore(restored, answered);
            Check(restored.DoorOrders.Find(manId).returning, "load replayed a demand already answered");
            trip = restored.DoorOrders.Find(manId);
            restored.Find(leader.Id).Status = CharacterStatus.Jailed;
            Check(restored.DoorOrders.Retained(restored, trip), "a hood required his lieutenant to travel with him");
            restored.Find(manId).Status = CharacterStatus.Jailed;
            Check(!restored.DoorOrders.Retained(restored, trip), "custody kept the errand active");
            restored.Find(manId).Status = CharacterStatus.Dead;
            Check(!restored.DoorOrders.Retained(restored, trip), "a dead man kept the errand");
            restored.Find(manId).Status = CharacterStatus.Active;
            restored.DoorOrders.ObserveBody(manId, 0, true);
            Check(!restored.DoorOrders.Retained(restored, trip),
                "a lost corpse restarted its errand before the delayed roster death report");
            restored.DoorOrders.ObserveBody(manId, 3, false);
            Check(!restored.DoorOrders.Retained(restored, trip), "a stale living view undid a casualty");
            trip.cancelled = false;
            restored.Organization.BlockResponsibilities.Clear();
            restored.Organization.BlockResponsibilities.Add(new OrganizationBlockResponsibility(block, other.Id));
            Check(!restored.DoorOrders.Retained(restored, trip), "a reassigned block kept the previous branch's errand");
            answered.doorOrders = null; RosterSnapshot.Restore(restored, answered);
            Check(restored.DoorOrders.Orders.Count == 0, "an old save retained the previous session's errands");

            DoorMenu.Forget(); DoorMenu.ToggleCrew(own.Id); DoorMenu.TogglePerson(manId);
            Check(DoorMenu.SelectedCrewId < 0 && DoorMenu.SelectedPersonId == manId,
                "crew and solo selections could be active together");
            DoorMenu.ConstrainToBlock(roster, block);
            Check(DoorMenu.SelectedPersonId == manId, "a valid reserve pick disappeared");
            DoorMenu.ToggleCrew(own.Id);
            Check(DoorMenu.SelectedPersonId < 0, "choosing a crew retained the individual");
            DoorMenu.Forget();
            return failures;
        }
    }
}
