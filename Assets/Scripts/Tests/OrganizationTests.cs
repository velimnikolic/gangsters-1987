using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Gangs;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Headless contracts for GAN-55 / ORG-001 through ORG-012 and ORG-014.</summary>
    public static class OrganizationTests
    {
        static readonly TerritoryBlockId BlockA = new TerritoryBlockId("core:test:block:a");
        static readonly TerritoryBlockId BlockB = new TerritoryBlockId("core:test:block:b");

        public static List<string> Run()
        {
            var failures = new List<string>();
            BossIsOneRealStableCharacter(failures);
            RecruitmentPaysThenCreatesOneUnassignedHood(failures);
            HierarchyTransfersIdentityAndHasNoFourHoodLimit(failures);
            CapacityIsCentralHardForMenSoftForBlocks(failures);
            ResponsibilityUsesCanonicalIdsWithoutChangingTerritorySignals(failures);
            QueryProjectsHierarchyAndPhysicalMappings(failures);
            ValidationReportsCorruptionWithoutRepairingIt(failures);
            FilingOfficeAnswersOnlyAfterItsDelay(failures);
            FilingOfficeIsWhereCapacityIsHard(failures);
            return failures;
        }

        /// <summary>The sheet ASKS and the outfit ANSWERS: nothing the resolver does may
        /// happen at the moment the order is filed, and it must happen exactly once.</summary>
        static void FilingOfficeAnswersOnlyAfterItsDelay(List<string> failures)
        {
            var office = new OutfitFilings { RulingSeconds = 1f };
            var ran = 0;
            var filing = office.File("D1 09:00", "A man put under Artie Byrne.", () =>
            {
                ran++;
                return FilingRuling.Grant("he reports to him from today");
            });

            if (ran != 0 || filing.Status != FilingStatus.Filed || office.AwaitingCount != 1)
                failures.Add("Filings: an order took effect at the moment it was filed.");

            if (office.Tick(0.5f) || ran != 0 || filing.Status != FilingStatus.Filed)
                failures.Add("Filings: the office answered before its delay ran out.");

            if (!office.Tick(0.6f) || ran != 1 ||
                filing.Status != FilingStatus.Granted ||
                filing.Ruling != "he reports to him from today")
                failures.Add("Filings: the ruling did not land exactly once.");

            if (office.Tick(10f) || ran != 1 || office.AwaitingCount != 0)
                failures.Add("Filings: a settled order was ruled on a second time.");

            // Newest first, and both answered in the order they were asked.
            var order = new List<int>();
            var second = new OutfitFilings { RulingSeconds = 0.5f };
            second.File("D1 09:01", "first", () =>
            {
                order.Add(1);
                return FilingRuling.Grant("");
            });
            second.File("D1 09:02", "second", () =>
            {
                order.Add(2);
                return FilingRuling.Refuse("no room");
            });
            second.Tick(1f);
            if (order.Count != 2 || order[0] != 1 || order[1] != 2)
                failures.Add("Filings: orders were not answered in the order they were asked.");
            if (second.All.Count != 2 || second.All[0].Text != "second" ||
                second.All[0].Status != FilingStatus.Refused)
                failures.Add("Filings: the sheet does not read newest order first.");
        }

        /// <summary>The filing office and the roster refuse the ASSIGNMENT that would
        /// create an overage (RANK-001) - but an overage that arises without one (the
        /// config tightening, a succession) is CARRIED, not repaired: nobody is thrown
        /// off a branch by arithmetic.</summary>
        static void FilingOfficeIsWhereCapacityIsHard(List<string> failures)
        {
            var atLimit = new CapacityMeasure(3, 3);
            var room = new CapacityMeasure(2, 3);
            var over = new CapacityMeasure(4, 3);

            if (OutfitFilingRules.AcceptsAnotherMan(atLimit) ||
                OutfitFilingRules.AcceptsAnotherMan(over) ||
                !OutfitFilingRules.AcceptsAnotherMan(room))
                failures.Add("Filings: the office does not refuse the man who would not fit.");
            if (OutfitFilingRules.AcceptsAnotherBlock(atLimit) ||
                !OutfitFilingRules.AcceptsAnotherBlock(room))
                failures.Add("Filings: the office does not refuse the block that would not fit.");

            if (!OutfitFilingRules.ManRefusal("Artie Byrne", atLimit).Contains("3/3") ||
                !OutfitFilingRules.BlockRefusal("Artie Byrne", atLimit).Contains("3/3"))
                failures.Add("Filings: a refusal does not print the figure it refused on.");

            // An overage the ROSTER never assented to - the ceiling dropped under men
            // already standing - is carried and read, and the men stay where they are.
            var roster = RosterSeeder.GenerateStaffed(23);
            var crew = roster.Crews[0];
            var standing = crew.HoodIds.Count;
            RosterOps.ConfigureOrganization(roster, new OrganizationLimits(70, 4, 1, 1));
            var capacity = new OrganizationQuery(roster).CapacityOf(crew.LieutenantId);
            if (standing > 1 && !capacity.Manpower.IsOverCapacity)
                failures.Add("Filings: a config-shrunk overage is not read as one.");
            if (crew.HoodIds.Count != standing)
                failures.Add("Filings: shrinking the ceiling threw men off the branch.");
        }

        static void RecruitmentPaysThenCreatesOneUnassignedHood(List<string> failures)
        {
            if (PersonnelDirector.DefaultHoodRecruitmentCost != 50)
                failures.Add("Recruitment: the configurable Phase-1 default is not $50.");

            var roster = RosterSeeder.GenerateStaffed(1987);
            var accounts = new Accounts();
            accounts.Open(1);
            var beforeCount = roster.Members.Count;
            var beforeCrewCount = roster.Crews.Count;
            var beforeSafe = accounts.Safe;

            OpResult Purchase(int price, string _)
            {
                var refusal = BalanceMath.TryPurchase(accounts, price);
                return refusal == null ? OpResult.Success : OpResult.Fail(refusal);
            }

            var result = HoodRecruitmentAuthority.Execute(
                roster,
                new System.Random(1401),
                PersonnelDirector.DefaultHoodRecruitmentCost,
                Purchase,
                out var recruit);
            var query = new OrganizationQuery(roster);
            var hoods = new List<OrganizationPerson>();
            query.CollectHoods(hoods);
            var snapshot = hoods.Find(person => recruit != null && person.Id == recruit.Id);

            if (!result.Ok || recruit == null || roster.Members.Count != beforeCount + 1 ||
                roster.Crews.Count != beforeCrewCount)
                failures.Add("Recruitment: a paid intent did not create exactly one Character.");
            else if (recruit.Rank != Rank.Hood || recruit.Specialty != Specialty.None ||
                     roster.AssignmentOf(recruit.Id).Kind != AssignmentKind.Pool ||
                     !snapshot.IsValid || !snapshot.IsUnassigned)
                failures.Add("Recruitment: the new Character is not an unassigned Hood.");
            else if (!query.TryGetCommandParent(recruit.Id, out var parent) ||
                     parent.Id != roster.BossId)
                failures.Add("Recruitment: the available Hood does not report directly to the Boss.");
            else if (string.IsNullOrEmpty(recruit.FirstName) ||
                     string.IsNullOrEmpty(recruit.Surname) || string.IsNullOrEmpty(recruit.Look))
                failures.Add("Recruitment: identity/name/appearance was not generated by authority.");

            var rolledStats = new HashSet<int>();
            if (recruit != null)
                for (var attribute = 0; attribute < AttributeScale.Count; attribute++)
                {
                    var value = recruit.GetHalfSteps((CharacterAttribute)attribute);
                    rolledStats.Add(value);
                    if (value < AttributeScale.MinHalfSteps ||
                        value > RosterSeeder.RecruitCeilingHalfSteps)
                        failures.Add("Recruitment: a starting stat was outside the recruit roll band.");
                }
            if (recruit != null && rolledStats.Count < 2)
                failures.Add("Recruitment: starting stats were not randomized.");

            if (accounts.Safe != beforeSafe - PersonnelDirector.DefaultHoodRecruitmentCost ||
                accounts.Current == null ||
                accounts.Current.Purchases != PersonnelDirector.DefaultHoodRecruitmentCost)
                failures.Add("Recruitment: the authoritative account did not book the $50 cost.");

            var poorRoster = RosterSeeder.GenerateStaffed(1988);
            var poorAccounts = new Accounts { Safe = 49 };
            var poorCount = poorRoster.Members.Count;
            OpResult Refuse(int price, string _)
            {
                var refusal = BalanceMath.TryPurchase(poorAccounts, price);
                return refusal == null ? OpResult.Success : OpResult.Fail(refusal);
            }

            var refused = HoodRecruitmentAuthority.Execute(
                poorRoster,
                new System.Random(1402),
                PersonnelDirector.DefaultHoodRecruitmentCost,
                Refuse,
                out var unpaid);
            if (refused.Ok || unpaid != null || poorRoster.Members.Count != poorCount ||
                poorAccounts.Safe != 49)
                failures.Add("Recruitment: insufficient funds still changed money or personnel.");
        }

        static void BossIsOneRealStableCharacter(List<string> failures)
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var roster = RosterSeeder.GenerateStaffed(seed);
                var boss = roster.FindBoss();
                var bosses = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Rank == Rank.Boss)
                        bosses++;

                if (bosses != 1 || boss == null || boss.Id != RosterSeeder.FixtureBossCharacterId)
                    failures.Add("Boss: seed " + seed + " did not produce exactly one stable Boss.");
                else if (boss.FullName != GangCatalog.BossName || boss.Look != GangCatalog.BossModel)
                    failures.Add("Boss: canonical Don Salvatore identity/model was not reused.");

                // Adding him consumes no personnel draws and therefore never moves the
                // six pre-existing IDs off 0..5.
                for (var id = 0; id < RosterSeeder.FixtureStaffCount; id++)
                    if (roster.Find(id) == null || roster.Find(id).Rank == Rank.Boss)
                        failures.Add("Boss: existing staff ID " + id + " was displaced.");
            }

            var large = RosterSeeder.GenerateLarge(1987, 60);
            var largeBoss = large.FindBoss();
            if (largeBoss == null || largeBoss.Id != RosterSeeder.FixtureBossCharacterId)
                failures.Add("Boss: scale fixture changed the canonical Boss Character ID.");
            var validation = new List<string>();
            OrganizationValidator.Validate(
                large, new HashSet<TerritoryBlockId>(), null, validation);
            if (validation.Count != 0)
                failures.Add("Boss: scale hierarchy reports " + validation[0]);
        }

        static void HierarchyTransfersIdentityAndHasNoFourHoodLimit(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var query = new OrganizationQuery(roster);
            var crew = roster.Crews[0];
            var lieutenant = roster.Find(crew.LieutenantId);
            var direct = new List<OrganizationPerson>();
            query.CollectDirectSubordinates(roster.BossId, direct);
            var hood = direct.Find(person => person.Rank == Rank.Hood);
            var original = roster.Find(hood.Id);
            original.Look = "stable-look";
            original.ConditionNote = "stable-state";

            var moved = RosterOps.AssignToCrew(roster, hood.Id, crew.Id);
            if (!moved.Ok || roster.Find(hood.Id) != original ||
                original.Look != "stable-look" || original.ConditionNote != "stable-state")
                failures.Add("Hierarchy: transfer cloned or changed the Character record.");
            if (!query.TryGetCommandParent(hood.Id, out var parent) ||
                parent.Id != lieutenant.Id)
                failures.Add("Hierarchy: Lieutenant did not become the one direct parent.");
            if (RosterOps.AssignToCrew(roster, hood.Id, crew.Id).Ok)
                failures.Add("Hierarchy: duplicate parent assignment was accepted.");

            var rng = new System.Random(1987);
            while (crew.HoodIds.Count <= Crew.MaxTacticalHoods + 2)
            {
                var recruit = RosterSeeder.Recruit(roster, rng);
                if (!RosterOps.AssignToCrew(roster, recruit.Id, crew.Id).Ok)
                {
                    failures.Add("Hierarchy: organization still refuses the fifth Hood.");
                    break;
                }
            }
            if (crew.HoodIds.Count <= Crew.MaxTacticalHoods)
                failures.Add("Hierarchy: organizational manpower is still the tactical group.");

            var back = RosterOps.AssignToBoss(roster, hood.Id, roster.BossId);
            if (!back.Ok || !query.TryGetCommandParent(hood.Id, out parent) ||
                parent.Id != roster.BossId || crew.HoodIds.Contains(hood.Id))
                failures.Add("Hierarchy: Boss/Lieutenant transfer left two parents.");
        }

        /// <summary>The numbers live in ONE serialized config; a lieutenant's manpower
        /// cap is HARD at the roster (RANK-001 - he refuses the next man), and block
        /// responsibility stays soft behind its force flag. This test used to assert a
        /// soft manpower overage and looped forever when RANK-001 made the cap hard -
        /// every loop below is guarded on principle.</summary>
        static void CapacityIsCentralHardForMenSoftForBlocks(List<string> failures)
        {
            var defaults = OrganizationLimits.Default;
            if (defaults.BossManpower != 70 || defaults.BossBlocks != 4 ||
                defaults.LieutenantManpower != 50 || defaults.LieutenantBlocks != 3)
                failures.Add("Capacity: canonical defaults moved or became scattered.");

            var roster = RosterSeeder.GenerateStaffed(7);
            RosterOps.ConfigureOrganization(roster, new OrganizationLimits(70, 4, 50, 1));
            var crew = roster.Crews[0];
            var rng = new System.Random(7);
            var query = new OrganizationQuery(roster);

            var manpower = query.CapacityOf(crew.LieutenantId).Manpower;
            if (manpower.Current > manpower.Maximum)
            {
                failures.Add("Capacity: the seed dealt a branch already past its cap.");
                return;
            }

            // Fill the branch to its own derived cap...
            var guard = manpower.Maximum - manpower.Current + 2;
            while (guard-- > 0 &&
                   query.CapacityOf(crew.LieutenantId).Manpower.Current <
                   query.CapacityOf(crew.LieutenantId).Manpower.Maximum)
            {
                var hood = RosterSeeder.Recruit(roster, rng);
                if (!RosterOps.AssignToCrew(roster, hood.Id, crew.Id).Ok)
                {
                    failures.Add("Capacity: the branch refused a man below its cap.");
                    return;
                }
            }

            // ...and the NEXT man is refused at the roster itself (RANK-001).
            var extra = RosterSeeder.Recruit(roster, rng);
            if (RosterOps.AssignToCrew(roster, extra.Id, crew.Id).Ok)
                failures.Add("Capacity: the lieutenant took a man past his manpower cap.");
            var atCap = query.CapacityOf(crew.LieutenantId).Manpower;
            if (atCap.Current != atCap.Maximum || atCap.IsOverCapacity)
                failures.Add("Capacity: manpower did not read full-at-cap after the refusal.");

            // Block responsibility is hard at the cap the same way: ground he cannot
            // carry is ground the outfit does not really hold (RANK-001).
            if (!RosterOps.AssignBlockResponsibility(
                    roster, BlockA, crew.LieutenantId, true).Ok)
                failures.Add("Capacity: a block below the cap was refused.");
            if (RosterOps.AssignBlockResponsibility(
                    roster, BlockB, crew.LieutenantId, true).Ok)
                failures.Add("Capacity: the lieutenant took a block past his cap.");
            var blocks = query.CapacityOf(crew.LieutenantId).Blocks;
            if (blocks.Current != 1 || blocks.Maximum != 1 || blocks.IsOverCapacity)
                failures.Add("Capacity: block current/max is wrong at the cap.");
        }

        static void ResponsibilityUsesCanonicalIdsWithoutChangingTerritorySignals(
            List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(11);
            var lieutenantId = roster.Crews[0].LieutenantId;
            var unknown = RosterOps.AssignBlockResponsibility(
                roster, new TerritoryBlockId("missing"), lieutenantId, false);
            if (unknown.Ok)
                failures.Add("Responsibility: an unknown canonical block was accepted.");

            var definition = new TerritoryBlockDefinition(
                BlockA, 1, new TerritoryNeighborhoodId("test"), "Test", "Block A",
                new TerritoryBounds(0f, 0f, 10f, 10f), "test");
            var territory = new TerritorySimulationState(new[] { definition });
            var territoryVersion = territory.Version;

            if (!RosterOps.AssignBlockResponsibility(roster, BlockA, lieutenantId, true).Ok)
                failures.Add("Responsibility: valid Lieutenant assignment was refused.");
            if (territory.Version != territoryVersion)
                failures.Add("Responsibility: administration mutated territory simulation state.");

            if (!RosterOps.AssignBlockResponsibility(roster, BlockA, roster.BossId, true).Ok)
                failures.Add("Responsibility: reassignment to the Boss was refused.");
            var query = new OrganizationQuery(roster);
            var blocks = new List<OrganizationBlockResponsibility>();
            query.CollectBlockResponsibilities(roster.BossId, blocks);
            if (blocks.Count != 1 || blocks[0].BlockId != BlockA)
                failures.Add("Responsibility: canonical ID/reassignment did not persist.");

            if (!RosterOps.RemoveBlockResponsibility(roster, BlockA, roster.BossId).Ok)
                failures.Add("Responsibility: remove through the authority was refused.");
            query.CollectBlockResponsibilities(roster.BossId, blocks);
            if (blocks.Count != 0)
                failures.Add("Responsibility: removed block still consumes capacity.");
        }

        static void QueryProjectsHierarchyAndPhysicalMappings(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(13);
            var crew = roster.Crews[0];
            var ids = new[] { crew.LieutenantId, crew.HoodIds[0] };
            var mapping = new TacticalPersonnelMapping(crew.Id, crew.LieutenantId, ids);
            ids[0] = -1; // the read model must not retain a caller-owned mutable array
            var source = new FakePhysicalSource(mapping);
            var query = new OrganizationQuery(roster);
            query.BindPhysical(source);

            var lieutenants = new List<OrganizationPerson>();
            query.CollectLieutenants(lieutenants);
            var physical = new List<TacticalPersonnelMapping>();
            query.CollectPhysicalMappings(physical);
            if (lieutenants.Count != 1 || physical.Count != 1 ||
                physical[0].CommandParentId != lieutenants[0].Id ||
                physical[0].PersonnelIds.Count != 2)
                failures.Add("Query: hierarchy/physical read model is incomplete.");

            var validation = new List<string>();
            OrganizationValidator.Validate(
                roster, new HashSet<TerritoryBlockId>(), source, validation);
            if (validation.Count != 0)
                failures.Add("Query: valid physical mapping reports " + validation[0]);
        }

        static void ValidationReportsCorruptionWithoutRepairingIt(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(17);
            var crew = roster.Crews[0];
            var duplicate = crew.HoodIds[0];
            roster.Organization.BossHoodIds.Add(duplicate);
            roster.Organization.BlockResponsibilities.Add(
                new OrganizationBlockResponsibility(BlockB, duplicate));
            // Deliberately cross the two legal ranks into a Boss <-> Lieutenant loop.
            crew.HoodIds.Add(roster.BossId);
            roster.Organization.BossHoodIds.Add(crew.LieutenantId);

            var beforeBossRows = roster.Organization.BossHoodIds.Count;
            var beforeBlocks = roster.Organization.BlockResponsibilities.Count;
            var stale = new FakePhysicalSource(
                new TacticalPersonnelMapping(99, 999,
                    new[] { 999, 999, 999, 999, 999, 999 }));
            var validation = new List<string>();
            OrganizationValidator.Validate(
                roster, new HashSet<TerritoryBlockId> { BlockA }, stale, validation);

            if (!Contains(validation, "direct command parents") ||
                !Contains(validation, "hierarchy cycle") ||
                !Contains(validation, "unknown block") ||
                !Contains(validation, "stale command parent") ||
                !Contains(validation, "maximum physical projection") ||
                !Contains(validation, "unavailable Character"))
                failures.Add("Validation: corruption did not produce actionable diagnostics.");
            if (roster.Organization.BossHoodIds.Count != beforeBossRows ||
                roster.Organization.BlockResponsibilities.Count != beforeBlocks)
                failures.Add("Validation: diagnostics silently repaired destructive state.");
        }

        static bool Contains(List<string> rows, string fragment)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Contains(fragment))
                    return true;
            return false;
        }

        sealed class FakePhysicalSource : IOrganizationPhysicalSource
        {
            readonly TacticalPersonnelMapping mapping;

            public FakePhysicalSource(TacticalPersonnelMapping mapping) =>
                this.mapping = mapping;

            public void CollectPhysicalMappings(List<TacticalPersonnelMapping> into) =>
                into.Add(mapping);
        }
    }
}
