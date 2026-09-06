using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Personnel
{
    /// <summary>Admission and roster scope shared by mission pickers and dispatch.</summary>
    public static class BlockMissionChoice
    {
        public static int ResponsibleLeader(Roster roster, TerritoryBlockId block)
        {
            if (roster == null || !block.IsValid) return -1;
            foreach (var row in roster.Organization.BlockResponsibilities)
                if (row.BlockId == block) return row.LeaderId;
            return -1;
        }

        public static Crew ResponsibleCrew(Roster roster, TerritoryBlockId block)
        {
            var leader = ResponsibleLeader(roster, block);
            if (leader < 0) return null;
            foreach (var crew in roster.Crews)
                if (crew.LieutenantId == leader) return crew;
            return null;
        }

        public static string Refusal(Roster roster, TerritoryBlockId block, int crewId,
            bool restrictToResponsible)
        {
            var crew = roster?.FindCrew(crewId);
            if (crew == null) return "there is no crew to send";
            var responsible = restrictToResponsible ? ResponsibleLeader(roster, block) : -1;
            if (responsible >= 0 && crew.LieutenantId != responsible)
                return "only the crew responsible for this block can take its orders";
            var leader = roster.Find(crew.LieutenantId);
            if (leader == null || leader.Gone || leader.Status != CharacterStatus.Active)
                return "the crew's leader is unavailable";
            return null;
        }

        public static int ParentOf(Roster roster, int characterId)
        {
            var man = roster?.Find(characterId);
            if (man == null || man.Gone) return -1;
            var crew = roster.CrewOf(characterId);
            if (crew != null) return crew.LieutenantId;
            return man.Rank == Rank.Hood && man.Specialty == Specialty.None &&
                   roster.AssignmentOf(characterId).Kind == AssignmentKind.Pool
                ? roster.BossId : -1;
        }

        public static string BagRefusal(Roster roster, TerritoryBlockId block, int characterId)
        {
            var crew = ResponsibleCrew(roster, block);
            var man = roster?.Find(characterId);
            if (crew == null) return "name who answers for this block first";
            if (man == null || man.Rank != Rank.Hood || roster.CrewOf(characterId) != crew)
                return "only a man from the responsible leader's roster can carry this bag";
            return null;
        }

        public static bool InStreetLine(Roster roster, Crew crew, int characterId,
            IReadOnlyList<TacticalPersonnelMapping> physical = null)
        {
            if (crew == null) return false;
            for (var i = 0; physical != null && i < physical.Count; i++)
            {
                var group = physical[i];
                if (group.IsDetachment || group.CommandParentId != crew.LieutenantId) continue;
                foreach (var id in group.PersonnelIds)
                    if (id == characterId) return true;
                return false;
            }
            var leader = roster.Find(crew.LieutenantId);
            if (leader == null || leader.Status != CharacterStatus.Active) return false;
            var count = 0;
            foreach (var id in crew.HoodIds)
            {
                var man = roster.Find(id);
                if (man == null || man.Gone || man.Status != CharacterStatus.Active || man.OutOfTown ||
                    roster.DoorOrders.Keeps(roster, id))
                    continue;
                if (id == characterId) return count < Crew.MaxTacticalHoods;
                count++;
            }
            return false;
        }

        public static string PersonRefusal(Roster roster, TerritoryBlockId block, int characterId,
            bool restricted, IReadOnlyList<TacticalPersonnelMapping> physical = null)
        {
            var man = roster?.Find(characterId);
            if (man == null || man.Gone || man.Status != CharacterStatus.Active || man.OutOfTown ||
                man.Rank != Rank.Hood || man.Specialty != Specialty.None || man.Duty != Duty.None)
                return "this man is unavailable for a doorstep errand";
            var parent = ParentOf(roster, characterId);
            var responsible = restricted ? ResponsibleLeader(roster, block) : -1;
            if (parent < 0 || (responsible >= 0 && parent != responsible))
                return "only the responsible leader's own men can take this order";
            if (InStreetLine(roster, roster.CrewOf(characterId), characterId, physical))
                return "this man goes with his lieutenant's crew";
            return null;
        }

        public static void Collect(Roster roster, TerritoryBlockId block,
            bool restrictToResponsible, List<Crew> crews, List<Character> reserve,
            IReadOnlyList<TacticalPersonnelMapping> physical = null)
        {
            crews.Clear(); reserve.Clear();
            if (roster == null) return;
            var responsible = restrictToResponsible ? ResponsibleLeader(roster, block) : -1;
            foreach (var crew in roster.Crews)
            {
                if (responsible >= 0 && crew.LieutenantId != responsible) continue;
                var leader = roster.Find(crew.LieutenantId);
                if (leader != null && !leader.Gone) crews.Add(crew);
            }
            foreach (var man in roster.Members)
                if (PersonRefusal(roster, block, man.Id, restrictToResponsible, physical) == null)
                    reserve.Add(man);
        }

        public static string Label(Roster roster, Crew crew) =>
            (roster?.Find(crew.LieutenantId)?.FullName ?? "Unknown lieutenant") + " + crew";
    }
}
