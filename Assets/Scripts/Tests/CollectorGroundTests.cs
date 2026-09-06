using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Collector admission, including old assignments whose ground was lost.</summary>
    public static class CollectorGroundTests
    {
        static readonly TerritoryBlockId Ground = new TerritoryBlockId("collector:ground");

        public static List<string> Run()
        {
            var failures = new List<string>();
            foreach (var boss in new[] { false, true })
            {
                var roster = Fixture(boss, out var crew);
                var man = crew.HoodIds[0];
                var line = string.Join(",", crew.HoodIds);
                Refused(RosterOps.SetDuty(roster, man, Duty.Collector), "direct duty", failures);
                Refused(RosterOps.NameCollector(roster, crew.Id, man), "named bag", failures);
                Refused(RosterOps.LetLieutenantPick(roster, crew.Id, out var picked),
                    "lieutenant pick", failures);
                if (picked != -1 || crew.BagId != -1 || crew.BagNamedByBoss ||
                    roster.Find(man).Duty != Duty.None || string.Join(",", crew.HoodIds) != line)
                    failures.Add("GROUND: a refused appointment changed the roster.");

                // Somebody else's paper and an invalid block do not give him a route.
                roster.Organization.BlockResponsibilities.Add(
                    new OrganizationBlockResponsibility(Ground, 999));
                roster.Organization.BlockResponsibilities.Add(
                    new OrganizationBlockResponsibility(default, crew.LieutenantId));
                Refused(RosterOps.NameCollector(roster, crew.Id, man), "unrelated ground", failures);
                roster.Organization.BlockResponsibilities.Clear();

                if (!RosterOps.AssignBlockResponsibility(roster, Ground, crew.LieutenantId, true).Ok ||
                    !RosterOps.NameCollector(roster, crew.Id, man).Ok || crew.BagId != man)
                    failures.Add("GROUND: a leader with a block could not name his collector.");
                // No business view, dues, clock or physical crew is needed to appoint him.
                var escort = crew.HoodIds[0];
                RosterOps.PostEscort(roster, crew.Id, escort);
                RosterOps.RemoveBlockResponsibility(roster, Ground);
                var replacement = crew.HoodIds[0];
                var before = State(roster, crew);
                Refused(RosterOps.NameCollector(roster, crew.Id, replacement), "replacement", failures);
                Refused(RosterOps.LetLieutenantPick(roster, crew.Id, out _), "replacement pick", failures);
                if (State(roster, crew) != before)
                    failures.Add("GROUND: refusal disturbed an existing collector, escort or ruling.");

                var copy = Roster.Create(roster.GangId);
                RosterSnapshot.Restore(copy, RosterSnapshot.Snapshot(roster));
                var copied = copy.FindCrew(crew.Id);
                if (State(copy, copied) != before)
                    failures.Add("GROUND: restoring an old bag assignment changed its state.");
                Refused(RosterOps.NameCollector(copy, crew.Id, replacement), "restored roster", failures);
                if (!RosterOps.TakeOffTheBag(copy, man).Ok || copied.BagId >= 0 ||
                    copied.EscortIds.Count != 0 || !copied.HoodIds.Contains(escort))
                    failures.Add("GROUND: a collector without ground could not return his detail to the line.");
                if (!RosterOps.SetDuty(roster, man, Duty.None).Ok || crew.BagId >= 0)
                    failures.Add("GROUND: clearing a duty without ground was refused.");

                RosterOps.AssignBlockResponsibility(roster, Ground, crew.LieutenantId, true);
                if (!RosterOps.LetLieutenantPick(roster, crew.Id, out picked).Ok || picked < 0)
                    failures.Add("GROUND: restoring responsibility did not reopen appointment.");
            }
            return failures;
        }

        static void Refused(OpResult result, string path, List<string> failures)
        {
            if (result.Ok || result.Reason != CollectorChoice.NoGroundReason)
                failures.Add("GROUND: " + path + " did not explain the missing block.");
        }

        static string State(Roster roster, Crew crew)
        {
            var state = crew.BagId + ":" + crew.BagNamedByBoss + ":" + crew.BagNamedId +
                ":" + string.Join(",", crew.HoodIds) + ":" + string.Join(",", crew.EscortIds);
            foreach (var member in roster.Members) state += ":" + member.Id + ":" + member.Duty;
            return state;
        }

        static Roster Fixture(bool boss, out Crew crew)
        {
            var roster = Roster.Create(0);
            var leader = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Test", Surname = "Leader",
                Rank = boss ? Rank.Boss : Rank.Lieutenant, Status = CharacterStatus.Active,
            };
            roster.Members.Add(leader);
            if (boss) roster.Organization.BossId = leader.Id;
            crew = new Crew { Id = roster.NextCrewId(), LieutenantId = leader.Id };
            roster.Crews.Add(crew);
            for (var i = 0; i < 3; i++)
            {
                var man = new Character
                {
                    Id = roster.NextCharacterId(), FirstName = "Test", Surname = "Hood" + i,
                    Rank = Rank.Hood, Status = CharacterStatus.Active,
                };
                roster.Members.Add(man);
                crew.HoodIds.Add(man.Id);
            }
            return roster;
        }
    }
}
