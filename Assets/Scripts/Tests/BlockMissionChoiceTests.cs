using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;
using LivingCity.UI;

namespace LivingCity.Tests
{
    public static class BlockMissionChoiceTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            var roster = RosterSeeder.GenerateStaffed(31);
            var first = roster.Crews[0];
            var leader = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Other", Surname = "Lieutenant",
                Rank = Rank.Lieutenant, Status = CharacterStatus.Active,
            };
            roster.Members.Add(leader);
            var second = new Crew { Id = roster.NextCrewId(), LieutenantId = leader.Id };
            roster.Crews.Add(second);
            var free = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Free", Surname = "Hood",
                Rank = Rank.Hood, Status = CharacterStatus.Active,
            };
            roster.Members.Add(free);
            var block = new TerritoryBlockId("mission:assigned");
            var crews = new List<Crew>();
            var reserve = new List<Character>();
            BlockMissionChoice.Collect(roster, block, true, crews, reserve);
            if (!crews.Contains(first) || !crews.Contains(second) || !reserve.Contains(free))
                failures.Add("MISSION: an unassigned block did not list crews and reserve separately.");
            foreach (var man in reserve)
                if (roster.CrewOf(man.Id) != null)
                    failures.Add("MISSION: a crew member was offered as an individual.");
            if (BlockMissionChoice.Label(roster, first) !=
                roster.Find(first.LieutenantId).FullName + " + crew")
                failures.Add("MISSION: the crew option did not name its lieutenant.");

            RosterOps.AssignBlockResponsibility(roster, block, first.LieutenantId, true);
            BlockMissionChoice.Collect(roster, block, true, crews, reserve);
            if (crews.Count != 1 || crews[0] != first || reserve.Count != 0)
                failures.Add("MISSION: an assigned block offered another leader's men.");
            if (BlockMissionChoice.Refusal(roster, block, first.Id, true) != null ||
                BlockMissionChoice.Refusal(roster, block, second.Id, true) == null)
                failures.Add("MISSION: explicit dispatch bypassed the block's responsible crew.");
            if (BlockMissionChoice.Refusal(roster, block, second.Id, false) != null)
                failures.Add("MISSION: a direct street order inherited the block file's restriction.");

            // A selection made before the block is reassigned must not survive as an
            // order to its former leader. Both UI surfaces use this one crew identity.
            DoorMenu.Forget();
            DoorMenu.ToggleCrew(first.Id);
            DoorMenu.ConstrainToBlock(roster, block);
            if (DoorMenu.SelectedCrewId != first.Id)
                failures.Add("MISSION: the responsible crew's selection was lost.");
            RosterOps.AssignBlockResponsibility(roster, block, second.LieutenantId, true);
            if (BlockMissionChoice.Refusal(roster, block, first.Id, true) == null)
                failures.Add("MISSION: a stale command remained admissible after reassignment.");
            DoorMenu.ConstrainToBlock(roster, block);
            if (DoorMenu.SelectedCrewId >= 0)
                failures.Add("MISSION: an old selection remained after the responsibility moved.");
            BlockMissionChoice.Collect(roster, block, true, crews, reserve);
            if (crews.Count != 1 || crews[0] != second)
                failures.Add("MISSION: the picker did not follow the new responsible leader.");

            leader.Status = CharacterStatus.Jailed;
            if (BlockMissionChoice.Refusal(roster, block, second.Id, true) == null)
                failures.Add("MISSION: an unavailable responsible leader was sent.");
            BlockMissionChoice.Collect(roster, block, true, crews, reserve);
            if (crews.Count != 1 || crews[0] != second || reserve.Count != 0)
                failures.Add("MISSION: an unavailable leader caused fallback to another branch.");
            leader.Status = CharacterStatus.Active;
            DoorMenu.ToggleCrew(second.Id);
            DoorMenu.ToggleCrew(second.Id);
            if (DoorMenu.SelectedCrewId >= 0)
                failures.Add("MISSION: toggling the selected crew did not clear the choice.");
            DoorMenu.Forget();
            failures.AddRange(SoloDoorOrdersTests.Run());
            return failures;
        }
    }
}
