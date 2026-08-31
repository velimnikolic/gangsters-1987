using System;
using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.UI;

namespace LivingCity.Tests
{
    /// <summary>
    /// The personnel ledger's model: seeding determinism, the derived-pool assignment
    /// rules, promotion and demotion, equipment exclusivity, and the RosterView shape the
    /// almanac paints. Same discipline as <see cref="GateTests"/>: a plain static class,
    /// failures returned as data, no UnityEngine anywhere - the whole Personnel core is
    /// engine-free on purpose, so this suite runs in a bare .NET host.
    /// </summary>
    public static class PersonnelTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            SameSeedSameRoster(failures);
            DifferentSeedsDiffer(failures);
            SeedShapeInvariants(failures);
            NamesComeFromTheSharedTables(failures);
            HalfStepScaleRoundTrips(failures);
            AssignmentOfIsExclusive(failures);
            PromoteCreatesEmptyCrew(failures);
            PromoteLowStatWarnsButAllows(failures);
            PromoteRefusals(failures);
            PromoteFromCrewLeavesTheCrew(failures);
            DemoteDisbandsToPool(failures);
            AssignMovesBetweenCrews(failures);
            AssignToFrontSwapsTheManager(failures);
            LieutenantCannotBeClickAssigned(failures);
            EquipmentIsExclusive(failures);
            GearFlowsThroughTheLieutenant(failures);
            KeysMoveBetweenLieutenants(failures);
            LieutenantDealsArmsByOrganization(failures);
            MotorcycleIsWheelsAndNotAGun(failures);
            FrontArmsTheGuards(failures);
            DeadReceiveNothing(failures);
            ViewGroupsInLedgerOrder(failures);
            ViewSortsWithinGroups(failures);
            ViewFiltersCompose(failures);
            ViewScalesToSixty(failures);
            LieutenantHasOwnRow(failures);
            LargeRosterShape(failures);
            LedgerTextIsExhaustive(failures);
            LabelsFitTheLedgerColumns(failures);
            PracticeCostsRiseWithTheStars(failures);
            PracticeOnlyBuysItsOwnTrade(failures);
            PracticeStopsAtFiveStars(failures);
            PracticeCarriesTwoStepsAtOnce(failures);
            RisingWagesFollowTheStars(failures);
            TheLaidUpStandUpOnTheirDay(failures);

            return failures;
        }

        // -------------------------------------------------------------- improvement

        static void PracticeCostsRiseWithTheStars(List<string> failures)
        {
            // To reach half-step n costs 2n: the first half-star is a fortnight of
            // work, the last is a career.
            if (Practice.CostOf(5) != 10 || Practice.CostOf(10) != 20)
                failures.Add("PracticeCostsRiseWithTheStars: the cost curve moved.");

            var roster = new Roster();
            var man = Make(roster, "Sal", "Renna");
            var at = man.GetHalfSteps(CharacterAttribute.Firearms);
            if (Practice.NextCost(man, CharacterAttribute.Firearms) != Practice.CostOf(at + 1))
                failures.Add("PracticeCostsRiseWithTheStars: the next step is mispriced.");

            // One point short buys nothing; the point that completes it buys the step.
            man.AddPractice(CharacterAttribute.Firearms, Practice.CostOf(at + 1) - 1);
            Practice.Convert(roster, null);
            if (man.GetHalfSteps(CharacterAttribute.Firearms) != at)
                failures.Add("PracticeCostsRiseWithTheStars: a short bank still bought a star.");

            var rises = new List<Improvement>();
            man.AddPractice(CharacterAttribute.Firearms, 1);
            Practice.Convert(roster, rises);
            if (man.GetHalfSteps(CharacterAttribute.Firearms) != at + 1)
                failures.Add("PracticeCostsRiseWithTheStars: a full bank bought nothing.");
            if (rises.Count != 1 || rises[0].CharacterId != man.Id ||
                rises[0].Attribute != CharacterAttribute.Firearms ||
                rises[0].HalfSteps != at + 1)
                failures.Add("PracticeCostsRiseWithTheStars: the rise was not recorded.");

            // The bank is spent, not merely read - a second midnight must buy nothing.
            if (man.GetPractice(CharacterAttribute.Firearms) != 0)
                failures.Add("PracticeCostsRiseWithTheStars: the points were not spent.");
        }

        static void PracticeOnlyBuysItsOwnTrade(List<string> failures)
        {
            var roster = new Roster();
            var man = Make(roster, "Vito", "Carre");
            var driving = man.GetHalfSteps(CharacterAttribute.Driving);

            man.AddPractice(CharacterAttribute.Intimidation, 400);
            Practice.Convert(roster, null);

            if (man.GetHalfSteps(CharacterAttribute.Driving) != driving)
                failures.Add("PracticeOnlyBuysItsOwnTrade: leaning on shopkeepers " +
                             "taught him to drive.");
            if (man.GetHalfSteps(CharacterAttribute.Intimidation) !=
                AttributeScale.MaxHalfSteps)
                failures.Add("PracticeOnlyBuysItsOwnTrade: the trade he practised " +
                             "did not top out.");
        }

        static void PracticeStopsAtFiveStars(List<string> failures)
        {
            var roster = new Roster();
            var man = Make(roster, "Nick", "Pasca");
            man.SetHalfSteps(CharacterAttribute.Fists, AttributeScale.MaxHalfSteps);

            if (Practice.NextCost(man, CharacterAttribute.Fists) != 0)
                failures.Add("PracticeStopsAtFiveStars: a five-star man still has a price.");

            man.AddPractice(CharacterAttribute.Fists, 1_000);
            var rises = new List<Improvement>();
            Practice.Convert(roster, rises);
            if (man.GetHalfSteps(CharacterAttribute.Fists) != AttributeScale.MaxHalfSteps ||
                rises.Count != 0)
                failures.Add("PracticeStopsAtFiveStars: the scale went past five stars.");
        }

        static void PracticeCarriesTwoStepsAtOnce(List<string> failures)
        {
            var roster = new Roster();
            var man = Make(roster, "Enzo", "Bardi");
            man.SetHalfSteps(CharacterAttribute.Arson, AttributeScale.MinHalfSteps);

            // Enough for both the third and the fourth half-step, banked in one go.
            man.AddPractice(CharacterAttribute.Arson,
                Practice.CostOf(3) + Practice.CostOf(4));
            var rises = new List<Improvement>();
            Practice.Convert(roster, rises);

            if (man.GetHalfSteps(CharacterAttribute.Arson) != 4 || rises.Count != 2)
                failures.Add("PracticeCarriesTwoStepsAtOnce: a big job was worth one step.");
        }

        static void RisingWagesFollowTheStars(List<string> failures)
        {
            var roster = new Roster();
            var man = Make(roster, "Gino", "Rossi");
            var before = Wages.WageFor(man);

            man.AddPractice(CharacterAttribute.Stealth, Practice.CostOf(
                man.GetHalfSteps(CharacterAttribute.Stealth) + 1));
            Practice.Convert(roster, null);

            // Training men IS raising the payroll - the tension comes free from Wages
            // deriving at read, and this is the assertion that keeps it that way.
            if (Wages.WageFor(man) != before + Wages.HoodPerHalfStep)
                failures.Add("RisingWagesFollowTheStars: the wage did not follow the star.");
        }

        static void TheLaidUpStandUpOnTheirDay(List<string> failures)
        {
            var roster = new Roster();
            var man = Make(roster, "Rocco", "Vale");

            RosterOps.Hospitalize(roster, man.Id, backOnDay: 10);
            if (man.Status != CharacterStatus.Hospitalized || man.Gone)
                failures.Add("TheLaidUpStandUpOnTheirDay: a bed is not a grave.");

            if (RosterOps.Discharge(roster, 9) != 0 ||
                man.Status != CharacterStatus.Hospitalized)
                failures.Add("TheLaidUpStandUpOnTheirDay: discharged a day early.");

            if (RosterOps.Discharge(roster, 10) != 1 ||
                man.Status != CharacterStatus.Active || man.BackOnDay != 0)
                failures.Add("TheLaidUpStandUpOnTheirDay: he never got up.");

            // A man held at somebody else's pleasure has no date, and day one must not
            // read that as "due out" and empty every cell in the city.
            var held = Make(roster, "Aldo", "Riva");
            held.Status = CharacterStatus.Jailed;
            if (RosterOps.Discharge(roster, 1) != 0 ||
                held.Status != CharacterStatus.Jailed)
                failures.Add("TheLaidUpStandUpOnTheirDay: a dateless man walked out.");
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>A hand-built member with every attribute at 3.0 stars unless bent.</summary>
        static Character Make(Roster roster, string first, string last,
            Rank rank = Rank.Hood, Specialty specialty = Specialty.None,
            CharacterStatus status = CharacterStatus.Active)
        {
            var member = new Character
            {
                Id = roster.NextCharacterId(),
                FirstName = first,
                Surname = last,
                Rank = rank,
                Specialty = specialty,
                Status = status,
            };
            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a, 6);
            roster.Members.Add(member);
            return member;
        }

        static Crew MakeCrew(Roster roster, Character lieutenant, params Character[] hoods)
        {
            lieutenant.Rank = Rank.Lieutenant;
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            foreach (var hood in hoods)
                crew.HoodIds.Add(hood.Id);
            roster.Crews.Add(crew);
            return crew;
        }

        static RosterEquipment MakeItem(Roster roster, EquipmentKind kind)
        {
            var item = new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = kind,
                DisplayName = kind.ToString(),
            };
            roster.Equipment.Add(item);
            return item;
        }

        // -------------------------------------------------------------- determinism

        static void SameSeedSameRoster(List<string> failures)
        {
            var a = RosterSeeder.Generate(42);
            var b = RosterSeeder.Generate(42);

            if (a.Members.Count != b.Members.Count)
            {
                failures.Add("SameSeedSameRoster: member counts differ.");
                return;
            }

            for (var i = 0; i < a.Members.Count; i++)
            {
                var ma = a.Members[i];
                var mb = b.Members[i];
                if (ma.FullName != mb.FullName || ma.Rank != mb.Rank ||
                    ma.Loyalty != mb.Loyalty)
                    failures.Add($"SameSeedSameRoster: member {i} differs.");
                for (var s = 0; s < AttributeScale.Count; s++)
                    if (ma.GetHalfSteps((CharacterAttribute)s) !=
                        mb.GetHalfSteps((CharacterAttribute)s))
                        failures.Add($"SameSeedSameRoster: member {i} attribute {s} differs.");
            }

            if (a.FrontId != b.FrontId)
                failures.Add("SameSeedSameRoster: fronts differ.");
            if (a.Crews.Count != b.Crews.Count ||
                a.Crews[0].LieutenantId != b.Crews[0].LieutenantId)
                failures.Add("SameSeedSameRoster: crews differ.");
            for (var i = 0; i < a.Equipment.Count && i < b.Equipment.Count; i++)
                if (a.Equipment[i].DisplayName != b.Equipment[i].DisplayName)
                    failures.Add("SameSeedSameRoster: equipment differs.");
        }

        static void DifferentSeedsDiffer(List<string> failures)
        {
            var a = RosterSeeder.Generate(1);
            var b = RosterSeeder.Generate(2);

            var same = true;
            for (var i = 0; i < a.Members.Count; i++)
            {
                if (a.Members[i].FullName != b.Members[i].FullName)
                    same = false;
                for (var s = 0; s < AttributeScale.Count; s++)
                    if (a.Members[i].GetHalfSteps((CharacterAttribute)s) !=
                        b.Members[i].GetHalfSteps((CharacterAttribute)s))
                        same = false;
            }

            if (same)
                failures.Add("DifferentSeedsDiffer: seeds 1 and 2 rolled an identical six.");
        }

        static void SeedShapeInvariants(List<string> failures)
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var roster = RosterSeeder.Generate(seed);
                var tag = $"SeedShapeInvariants(seed {seed})";

                if (roster.Members.Count != RosterSeeder.MemberCount)
                {
                    failures.Add($"{tag}: {roster.Members.Count} members.");
                    continue;
                }

                var names = new HashSet<string>();
                var lieutenants = 0;
                var bosses = 0;
                foreach (var member in roster.Members)
                {
                    if (!names.Add(member.FullName))
                        failures.Add($"{tag}: duplicate name {member.FullName}.");
                    if (member.Rank == Rank.Lieutenant)
                        lieutenants++;
                    if (member.Rank == Rank.Boss)
                    {
                        bosses++;
                        if (member.Id != RosterSeeder.BossCharacterId ||
                            member.FullName != Gangs.GangCatalog.BossName ||
                            member.Look != Gangs.GangCatalog.BossModel ||
                            roster.BossId != member.Id)
                            failures.Add($"{tag}: Boss identity is not canonical.");
                        continue;
                    }
                    if (member.Specialty != Specialty.None)
                        failures.Add($"{tag}: specialist in the starting six.");
                    if (member.Status != CharacterStatus.Active || member.Wanted)
                        failures.Add($"{tag}: not everyone starts clean and active.");
                    if (member.Loyalty < 35 || member.Loyalty > 85)
                        failures.Add($"{tag}: loyalty {member.Loyalty} out of band.");
                    for (var a = 0; a < AttributeScale.Count; a++)
                    {
                        var v = member.GetHalfSteps((CharacterAttribute)a);
                        if (v < AttributeScale.MinHalfSteps || v > AttributeScale.MaxHalfSteps)
                            failures.Add($"{tag}: attribute {a} at {v} half-steps.");
                    }
                }

                if (lieutenants != 1)
                    failures.Add($"{tag}: {lieutenants} lieutenants.");
                if (bosses != 1)
                    failures.Add($"{tag}: {bosses} bosses.");
                if (roster.Crews.Count != 1)
                    failures.Add($"{tag}: {roster.Crews.Count} crews.");
                else
                {
                    var crew = roster.Crews[0];
                    if (crew.HoodIds.Count != 2)
                        failures.Add($"{tag}: crew of {crew.HoodIds.Count} hoods.");
                    var lieutenant = roster.Find(crew.LieutenantId);
                    if (lieutenant == null || lieutenant.Rank != Rank.Lieutenant)
                        failures.Add($"{tag}: the crew's head is not a lieutenant.");
                }

                var front = roster.FrontId >= 0 ? roster.Find(roster.FrontId) : null;
                if (front == null)
                    failures.Add($"{tag}: no front.");
                else if (front.Rank != Rank.Hood ||
                         roster.AssignmentOf(front.Id).Kind != AssignmentKind.Front)
                    failures.Add($"{tag}: the front is not a plain hood at the desk.");

                var pool = new List<int>();
                roster.PoolIds(pool);
                if (pool.Count != 2)
                    failures.Add($"{tag}: pool of {pool.Count}.");

                var pistols = 0;
                var vehicles = 0;
                foreach (var item in roster.Equipment)
                {
                    if (item.HolderId != RosterEquipment.Unheld)
                        failures.Add($"{tag}: starting stock already signed out.");
                    if (item.Kind == EquipmentKind.Pistol)
                        pistols++;
                    else
                        vehicles++;
                }
                if (pistols != RosterSeeder.PistolCount || vehicles != 1)
                    failures.Add($"{tag}: stock is {pistols} pistols / {vehicles} vehicles.");
            }
        }

        static void NamesComeFromTheSharedTables(List<string> failures)
        {
            var firsts = new HashSet<string>(PedestrianIdentity.AllMaleNames);
            var surnames = new HashSet<string>(PedestrianIdentity.AllSurnames);

            var roster = RosterSeeder.Generate(7);
            foreach (var member in roster.Members)
            {
                if (member.Rank == Rank.Boss)
                    continue;
                if (!firsts.Contains(member.FirstName))
                    failures.Add($"NamesComeFromTheSharedTables: {member.FirstName}.");
                if (!surnames.Contains(member.Surname))
                    failures.Add($"NamesComeFromTheSharedTables: {member.Surname}.");
            }
        }

        // -------------------------------------------------------------------- model

        static void HalfStepScaleRoundTrips(List<string> failures)
        {
            if (AttributeScale.Stars(2) != 1f || AttributeScale.Stars(5) != 2.5f ||
                AttributeScale.Stars(10) != 5f)
                failures.Add("HalfStepScaleRoundTrips: Stars() misconverts.");

            var roster = new Roster();
            var member = Make(roster, "Test", "Case");
            member.SetHalfSteps(CharacterAttribute.Fists, 0);
            if (member.GetHalfSteps(CharacterAttribute.Fists) != AttributeScale.MinHalfSteps)
                failures.Add("HalfStepScaleRoundTrips: no floor clamp.");
            member.SetHalfSteps(CharacterAttribute.Fists, 12);
            if (member.GetHalfSteps(CharacterAttribute.Fists) != AttributeScale.MaxHalfSteps)
                failures.Add("HalfStepScaleRoundTrips: no ceiling clamp.");
        }

        static void AssignmentOfIsExclusive(List<string> failures)
        {
            var roster = RosterSeeder.Generate(11);
            foreach (var member in roster.Members)
            {
                var assignment = roster.AssignmentOf(member.Id);
                var isFront = member.Id == roster.FrontId;
                var inCrews = 0;
                foreach (var crew in roster.Crews)
                    if (crew.LieutenantId == member.Id || crew.HoodIds.Contains(member.Id))
                        inCrews++;

                if (inCrews > 1)
                    failures.Add($"AssignmentOfIsExclusive: {member.FullName} in {inCrews} crews.");
                if (isFront && inCrews > 0)
                    failures.Add($"AssignmentOfIsExclusive: the front is also crewed.");

                var expected = member.Rank == Rank.Boss ? AssignmentKind.Boss
                    : isFront ? AssignmentKind.Front
                    : inCrews > 0 ? AssignmentKind.Crew
                    : AssignmentKind.Pool;
                if (assignment.Kind != expected)
                    failures.Add($"AssignmentOfIsExclusive: {member.FullName} reads " +
                                 $"{assignment.Kind}, expected {expected}.");
            }
        }

        // --------------------------------------------------------------- operations

        static void PromoteCreatesEmptyCrew(List<string> failures)
        {
            var roster = RosterSeeder.Generate(3);
            var pool = new List<int>();
            roster.PoolIds(pool);
            var id = pool[0];

            var result = RosterOps.Promote(roster, id, out var newCrewId);
            if (!result.Ok)
            {
                failures.Add("PromoteCreatesEmptyCrew: refused - " + result.Reason);
                return;
            }

            var crew = roster.FindCrew(newCrewId);
            if (crew == null || crew.LieutenantId != id || crew.HoodIds.Count != 0)
                failures.Add("PromoteCreatesEmptyCrew: no empty crew under the new man.");
            if (roster.Find(id).Rank != Rank.Lieutenant)
                failures.Add("PromoteCreatesEmptyCrew: rank unchanged.");

            roster.PoolIds(pool);
            if (pool.Contains(id))
                failures.Add("PromoteCreatesEmptyCrew: still in the pool.");
        }

        static void PromoteLowStatWarnsButAllows(List<string> failures)
        {
            var roster = new Roster();
            var hood = Make(roster, "Dim", "Fella");
            hood.SetHalfSteps(CharacterAttribute.Intelligence, 4); // 2.0 stars

            var check = RosterOps.CheckPromote(roster, hood.Id);
            if (!check.CanPromote || !check.LowStatWarning)
                failures.Add("PromoteLowStatWarnsButAllows: expected a warned-but-allowed check.");

            if (!RosterOps.Promote(roster, hood.Id, out _).Ok)
                failures.Add("PromoteLowStatWarnsButAllows: the player's mistake was blocked.");
        }

        static void PromoteRefusals(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = Make(roster, "Already", "Boss", Rank.Lieutenant);
            MakeCrew(roster, lieutenant);
            var accountant = Make(roster, "Book", "Keeper", specialty: Specialty.Accountant);
            var corpse = Make(roster, "Late", "Fella", status: CharacterStatus.Dead);

            foreach (var id in new[] { lieutenant.Id, accountant.Id, corpse.Id })
            {
                var check = RosterOps.CheckPromote(roster, id);
                if (check.CanPromote || check.Reason.Length == 0)
                    failures.Add($"PromoteRefusals: id {id} not refused with a reason.");
                if (RosterOps.Promote(roster, id, out _).Ok)
                    failures.Add($"PromoteRefusals: id {id} promoted anyway.");
            }
        }

        static void PromoteFromCrewLeavesTheCrew(List<string> failures)
        {
            var roster = RosterSeeder.Generate(5);
            var oldCrew = roster.Crews[0];
            var id = oldCrew.HoodIds[0];

            if (!RosterOps.Promote(roster, id, out _).Ok)
            {
                failures.Add("PromoteFromCrewLeavesTheCrew: refused.");
                return;
            }
            if (oldCrew.HoodIds.Contains(id))
                failures.Add("PromoteFromCrewLeavesTheCrew: still on the old crew's list.");
        }

        static void DemoteDisbandsToPool(List<string> failures)
        {
            var roster = RosterSeeder.Generate(9);
            var crew = roster.Crews[0];
            var lieutenantId = crew.LieutenantId;
            var hoods = new List<int>(crew.HoodIds);

            var result = RosterOps.Demote(roster, lieutenantId);
            if (!result.Ok)
            {
                failures.Add("DemoteDisbandsToPool: refused - " + result.Reason);
                return;
            }

            if (roster.Crews.Count != 0)
                failures.Add("DemoteDisbandsToPool: the crew survived.");
            if (roster.Find(lieutenantId).Rank != Rank.Hood)
                failures.Add("DemoteDisbandsToPool: rank kept.");

            foreach (var id in hoods)
                if (roster.AssignmentOf(id).Kind != AssignmentKind.Pool)
                    failures.Add($"DemoteDisbandsToPool: hood {id} not pooled.");
            if (roster.AssignmentOf(lieutenantId).Kind != AssignmentKind.Pool)
                failures.Add("DemoteDisbandsToPool: the ex-lieutenant not pooled.");
        }

        static void AssignMovesBetweenCrews(List<string> failures)
        {
            var roster = new Roster();
            var ltA = Make(roster, "Head", "Alpha", Rank.Lieutenant);
            var ltB = Make(roster, "Head", "Bravo", Rank.Lieutenant);
            var hood = Make(roster, "Foot", "Soldier");
            var crewA = MakeCrew(roster, ltA, hood);
            var crewB = MakeCrew(roster, ltB);

            var result = RosterOps.AssignToCrew(roster, hood.Id, crewB.Id);
            if (!result.Ok)
            {
                failures.Add("AssignMovesBetweenCrews: refused - " + result.Reason);
                return;
            }
            if (crewA.HoodIds.Contains(hood.Id))
                failures.Add("AssignMovesBetweenCrews: still in crew A.");
            if (!crewB.HoodIds.Contains(hood.Id))
                failures.Add("AssignMovesBetweenCrews: never arrived in crew B.");
        }

        static void AssignToFrontSwapsTheManager(List<string> failures)
        {
            var roster = new Roster();
            var first = Make(roster, "Old", "Desk");
            var second = Make(roster, "New", "Desk");
            roster.FrontId = first.Id;

            var result = RosterOps.AssignToFront(roster, second.Id);
            if (!result.Ok)
            {
                failures.Add("AssignToFrontSwapsTheManager: refused - " + result.Reason);
                return;
            }
            if (roster.FrontId != second.Id)
                failures.Add("AssignToFrontSwapsTheManager: front unchanged.");
            if (roster.AssignmentOf(first.Id).Kind != AssignmentKind.Pool)
                failures.Add("AssignToFrontSwapsTheManager: the old manager vanished.");
        }

        static void LieutenantCannotBeClickAssigned(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = Make(roster, "Head", "Crew", Rank.Lieutenant);
            var crew = MakeCrew(roster, lieutenant);

            if (RosterOps.AssignToCrew(roster, lieutenant.Id, crew.Id).Ok ||
                RosterOps.AssignToPool(roster, lieutenant.Id).Ok ||
                RosterOps.AssignToFront(roster, lieutenant.Id).Ok)
                failures.Add("LieutenantCannotBeClickAssigned: an assignment went through.");
        }

        static void EquipmentIsExclusive(List<string> failures)
        {
            // Lieutenants, because weapons refuse anyone below that rank now -
            // exclusivity is what is under test here, not the chain of command.
            var roster = new Roster();
            var a = Make(roster, "First", "Holder", Rank.Lieutenant);
            var b = Make(roster, "Second", "Holder", Rank.Lieutenant);
            var pistol = MakeItem(roster, EquipmentKind.Pistol);

            if (!RosterOps.GiveEquipment(roster, pistol.Id, a.Id).Ok)
                failures.Add("EquipmentIsExclusive: first grant refused.");
            if (RosterOps.GiveEquipment(roster, pistol.Id, b.Id).Ok)
                failures.Add("EquipmentIsExclusive: one pistol, two holders.");
            if (!RosterOps.ReturnEquipment(roster, pistol.Id).Ok)
                failures.Add("EquipmentIsExclusive: return refused.");
            if (!RosterOps.GiveEquipment(roster, pistol.Id, b.Id).Ok)
                failures.Add("EquipmentIsExclusive: refused after a clean return.");
        }

        /// <summary>ALL gear - guns and wheels alike - issues to lieutenants alone,
        /// with the refusal explaining the chain of command.</summary>
        static void GearFlowsThroughTheLieutenant(List<string> failures)
        {
            var roster = new Roster();
            var hood = Make(roster, "Corner", "Hood");
            var lieutenant = Make(roster, "Head", "Crew", Rank.Lieutenant);
            MakeCrew(roster, lieutenant, hood);
            var pistol = MakeItem(roster, EquipmentKind.Pistol);
            var car = MakeItem(roster, EquipmentKind.Vehicle);

            var refused = RosterOps.GiveEquipment(roster, pistol.Id, hood.Id);
            if (refused.Ok || refused.Reason != LedgerText.ReasonGearViaLieutenant)
                failures.Add("GearFlowsThroughTheLieutenant: the hood bought his " +
                             "own iron.");
            if (RosterOps.GiveEquipment(roster, car.Id, hood.Id).Ok)
                failures.Add("GearFlowsThroughTheLieutenant: the hood got his own " +
                             "car.");
            if (!RosterOps.GiveEquipment(roster, pistol.Id, lieutenant.Id).Ok ||
                !RosterOps.GiveEquipment(roster, car.Id, lieutenant.Id).Ok)
                failures.Add("GearFlowsThroughTheLieutenant: the lieutenant was " +
                             "refused.");
        }

        /// <summary>The street's hand-over: a car already dealt to one lieutenant goes
        /// to another on one order, no return to the safe in between - and the chain of
        /// command still holds, so a hood cannot be pointed at.</summary>
        static void KeysMoveBetweenLieutenants(List<string> failures)
        {
            var roster = new Roster();
            var first = Make(roster, "First", "Boss", Rank.Lieutenant);
            var second = Make(roster, "Second", "Boss", Rank.Lieutenant);
            var hood = Make(roster, "Corner", "Hood");
            MakeCrew(roster, first);
            MakeCrew(roster, second, hood);
            var car = MakeItem(roster, EquipmentKind.Vehicle);

            if (!RosterOps.GiveEquipment(roster, car.Id, first.Id).Ok)
                failures.Add("KeysMoveBetweenLieutenants: the first grant was refused.");
            if (!RosterOps.MoveEquipment(roster, car.Id, second.Id).Ok)
                failures.Add("KeysMoveBetweenLieutenants: a held car would not move.");
            if (car.OwnerId != second.Id)
                failures.Add("KeysMoveBetweenLieutenants: the deed did not change hands.");

            var again = RosterOps.MoveEquipment(roster, car.Id, second.Id);
            if (again.Ok || again.Reason != LedgerText.ReasonAlreadyHolds)
                failures.Add("KeysMoveBetweenLieutenants: his own car was handed to " +
                             "him twice.");

            var refused = RosterOps.MoveEquipment(roster, car.Id, hood.Id);
            if (refused.Ok || refused.Reason != LedgerText.ReasonGearViaLieutenant)
                failures.Add("KeysMoveBetweenLieutenants: a hood was given the keys.");

            // and the deal that runs right after leaves the car inside the new crew
            RosterOps.NormalizeArms(roster);
            if (car.OwnerId != second.Id ||
                (car.HolderId != second.Id && car.HolderId != hood.Id))
                failures.Add("KeysMoveBetweenLieutenants: the deal took the car out of " +
                             "the crew it was just given to.");
        }

        /// <summary>The deal itself: at five-star Organization the best iron lands in
        /// the best hands and the surplus stays with the lieutenant; at one star the
        /// hand is dealt backwards; a man who leaves the crew turns his iron in.</summary>
        static void LieutenantDealsArmsByOrganization(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = Make(roster, "Head", "Crew", Rank.Lieutenant);
            var ace = Make(roster, "Dead", "Eye");
            var mid = Make(roster, "Fair", "Shot");
            var mud = Make(roster, "Wild", "Miss");
            MakeCrew(roster, lieutenant, ace, mid, mud);

            lieutenant.SetHalfSteps(CharacterAttribute.Firearms, 2);
            ace.SetHalfSteps(CharacterAttribute.Firearms, 10);
            mid.SetHalfSteps(CharacterAttribute.Firearms, 8);
            mud.SetHalfSteps(CharacterAttribute.Firearms, 4);

            var tommy = MakeItem(roster, EquipmentKind.TommyGun);
            tommy.Value = 2000;
            var shotgun = MakeItem(roster, EquipmentKind.Shotgun);
            shotgun.Value = 900;
            var pistolA = MakeItem(roster, EquipmentKind.Pistol);
            pistolA.Value = 250;
            var pistolB = MakeItem(roster, EquipmentKind.Pistol);
            pistolB.Value = 100;
            var pistolC = MakeItem(roster, EquipmentKind.Pistol);
            pistolC.Value = 60;
            var safePistol = MakeItem(roster, EquipmentKind.Pistol);
            safePistol.Value = 40;

            foreach (var item in roster.Equipment)
                if (item != safePistol &&
                    !RosterOps.GiveEquipment(roster, item.Id, lieutenant.Id).Ok)
                    failures.Add("LieutenantDealsArmsByOrganization: the lieutenant " +
                                 "refused his own deck.");

            // The wheels deal runs on Driving, its own deck over the same hands.
            var car = MakeItem(roster, EquipmentKind.Vehicle);
            car.Value = 1500;
            if (!RosterOps.GiveEquipment(roster, car.Id, lieutenant.Id).Ok)
                failures.Add("LieutenantDealsArmsByOrganization: the car was refused.");
            lieutenant.SetHalfSteps(CharacterAttribute.Driving, 2);
            ace.SetHalfSteps(CharacterAttribute.Driving, 4);
            mid.SetHalfSteps(CharacterAttribute.Driving, 6);
            mud.SetHalfSteps(CharacterAttribute.Driving, 10);

            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 10);
            RosterOps.NormalizeArms(roster);
            if (tommy.HolderId != ace.Id || shotgun.HolderId != mid.Id ||
                pistolA.HolderId != mud.Id || pistolB.HolderId != lieutenant.Id)
                failures.Add("LieutenantDealsArmsByOrganization: the five-star deal " +
                             "misfired.");
            if (car.HolderId != mud.Id)
                failures.Add("LieutenantDealsArmsByOrganization: the car missed the " +
                             "best driver.");
            if (pistolC.HolderId != lieutenant.Id)
                failures.Add("LieutenantDealsArmsByOrganization: the surplus left " +
                             "the lieutenant.");
            if (safePistol.HolderId != RosterEquipment.Unheld)
                failures.Add("LieutenantDealsArmsByOrganization: the deal raided " +
                             "the safe.");

            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 2);
            RosterOps.NormalizeArms(roster);
            if (tommy.HolderId == ace.Id)
                failures.Add("LieutenantDealsArmsByOrganization: a one-star deal " +
                             "still found the ace.");
            if (pistolB.HolderId != ace.Id)
                failures.Add("LieutenantDealsArmsByOrganization: the backwards deal " +
                             "is not backwards.");

            // The ace walks to the pool. Gear stays in the PARENT - the user's rule:
            // he carries nothing out, and the crew's deal closes ranks over the men
            // who remain.
            var carried = tommy.HolderId == ace.Id || shotgun.HolderId == ace.Id ||
                pistolA.HolderId == ace.Id || pistolB.HolderId == ace.Id;
            if (!carried)
                failures.Add("LieutenantDealsArmsByOrganization: the ace left " +
                             "empty-handed before the walkout test.");
            RosterOps.AssignToPool(roster, ace.Id);
            RosterOps.NormalizeArms(roster);
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.HolderId == ace.Id)
                    failures.Add("LieutenantDealsArmsByOrganization: the ace walked " +
                                 "out with crew iron.");
                if (item != safePistol && item.OwnerId != lieutenant.Id)
                    failures.Add("LieutenantDealsArmsByOrganization: the crew lost " +
                                 "a deed in the walkout.");
                if (item != safePistol && item.HolderId != RosterEquipment.Unheld &&
                    roster.CrewOf(item.HolderId) == null &&
                    item.HolderId != lieutenant.Id)
                    failures.Add("LieutenantDealsArmsByOrganization: crew iron " +
                                 "landed outside the crew.");
            }
        }

        /// <summary>Gear dumped at the front deals to the men guarding the desk - the
        /// manager and the pooled hoods - ideally (the boss deals this one himself),
        /// with the surplus staying in the locker, never raiding a crew's deck; a
        /// guard who joins a crew takes his iron into that crew's deal.</summary>
        /// <summary>The counter sells motorcycles (ArmoryCatalog.Motorcycles), and a
        /// motorcycle is WHEELS: dealt by Driving with the cars, never by Firearms with
        /// the guns. The whole split is one predicate (RosterOps.IsWeapon) and it used
        /// to read "anything that is not a Vehicle", so the day the kind was added the
        /// quartermaster would have handed the outfit's best shot a moped to fire.</summary>
        static void MotorcycleIsWheelsAndNotAGun(List<string> failures)
        {
            if (RosterOps.IsWeapon(EquipmentKind.Motorcycle))
                failures.Add("MotorcycleIsWheelsAndNotAGun: a motorcycle counts as a gun.");

            var roster = new Roster();
            var lieutenant = Make(roster, "Sal", "Moretti", Rank.Lieutenant);
            var rider = Make(roster, "Bernie", "Carter");
            var shot = Make(roster, "Angelo", "Katz");
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            crew.HoodIds.Add(rider.Id);
            crew.HoodIds.Add(shot.Id);
            roster.Crews.Add(crew);

            // One who can ride and cannot shoot, one the other way round - so a deal by
            // the wrong stat lands on the wrong man and is visible.
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 10);
            lieutenant.SetHalfSteps(CharacterAttribute.Driving, 2);
            lieutenant.SetHalfSteps(CharacterAttribute.Firearms, 2);
            rider.SetHalfSteps(CharacterAttribute.Driving, 10);
            rider.SetHalfSteps(CharacterAttribute.Firearms, 2);
            shot.SetHalfSteps(CharacterAttribute.Driving, 2);
            shot.SetHalfSteps(CharacterAttribute.Firearms, 10);

            var bike = MakeItem(roster, EquipmentKind.Motorcycle);
            bike.Value = 1200;
            var tommy = MakeItem(roster, EquipmentKind.TommyGun);
            tommy.Value = 2000;

            if (!RosterOps.GiveEquipment(roster, bike.Id, lieutenant.Id).Ok)
                failures.Add("MotorcycleIsWheelsAndNotAGun: the machine would not issue " +
                             "to the lieutenant.");
            RosterOps.GiveEquipment(roster, tommy.Id, lieutenant.Id);
            RosterOps.NormalizeArms(roster);

            if (bike.HolderId != rider.Id)
                failures.Add("MotorcycleIsWheelsAndNotAGun: the machine missed the best " +
                             "driver.");
            if (tommy.HolderId != shot.Id)
                failures.Add("MotorcycleIsWheelsAndNotAGun: the gun deal was disturbed " +
                             "by the machine.");

            // And it is not the crew's transport: two men is not a crew, and the week's
            // travel arithmetic must not start pretending the outfit is mobile because
            // somebody bought a scooter.
            if (CrewKit.HasVehicle(roster, crew))
                failures.Add("MotorcycleIsWheelsAndNotAGun: a motorcycle counts as the " +
                             "crew's vehicle.");

            if (LedgerText.EquipmentLabel(EquipmentKind.Motorcycle).Length == 0)
                failures.Add("MotorcycleIsWheelsAndNotAGun: the kind has no ledger label.");
        }

        static void FrontArmsTheGuards(List<string> failures)
        {
            var roster = new Roster();
            var manager = Make(roster, "Desk", "Manager");
            var ace = Make(roster, "Pool", "Ace");
            var mud = Make(roster, "Pool", "Mud");
            var lieutenant = Make(roster, "Head", "Crew", Rank.Lieutenant);
            var crewHood = Make(roster, "Crew", "Hood");
            var crew = MakeCrew(roster, lieutenant, crewHood);

            if (!RosterOps.AssignToFront(roster, manager.Id).Ok)
            {
                failures.Add("FrontArmsTheGuards: could not seat the manager.");
                return;
            }

            manager.SetHalfSteps(CharacterAttribute.Firearms, 6);
            ace.SetHalfSteps(CharacterAttribute.Firearms, 10);
            mud.SetHalfSteps(CharacterAttribute.Firearms, 4);

            var crewGun = MakeItem(roster, EquipmentKind.Shotgun);
            crewGun.Value = 900;
            if (!RosterOps.GiveEquipment(roster, crewGun.Id, lieutenant.Id).Ok)
                failures.Add("FrontArmsTheGuards: the crew gun was refused.");

            var tommy = MakeItem(roster, EquipmentKind.TommyGun);
            tommy.Value = 2000;
            var pistolA = MakeItem(roster, EquipmentKind.Pistol);
            pistolA.Value = 250;
            var pistolB = MakeItem(roster, EquipmentKind.Pistol);
            pistolB.Value = 100;
            var spare = MakeItem(roster, EquipmentKind.Pistol);
            spare.Value = 60;

            foreach (var item in new[] { tommy, pistolA, pistolB, spare })
                if (!RosterOps.GiveEquipmentToFront(roster, item.Id).Ok)
                    failures.Add("FrontArmsTheGuards: the front refused the dump.");

            RosterOps.NormalizeArms(roster);

            // Boss's ideal deal over ace(10), manager(6), mud(4).
            if (tommy.HolderId != ace.Id || pistolA.HolderId != manager.Id ||
                pistolB.HolderId != mud.Id)
                failures.Add("FrontArmsTheGuards: the guards drew the wrong iron.");
            if (spare.HolderId != RosterEquipment.FrontArmory)
                failures.Add("FrontArmsTheGuards: the surplus left the locker.");
            if (crewGun.HolderId != lieutenant.Id)
                failures.Add("FrontArmsTheGuards: the front deal raided the crew.");

            // The ace joins a crew. The tommy STAYS at the front - gear belongs to
            // the parent - and the remaining guards draw over it; the ace's new
            // crew deals him ITS deck instead.
            RosterOps.AssignToCrew(roster, ace.Id, crew.Id);
            RosterOps.NormalizeArms(roster);
            if (tommy.OwnerId != RosterEquipment.FrontArmory)
                failures.Add("FrontArmsTheGuards: the front lost the tommy's deed.");
            if (tommy.HolderId == ace.Id)
                failures.Add("FrontArmsTheGuards: the ace walked out with front iron.");
            if (tommy.HolderId != manager.Id)
                failures.Add("FrontArmsTheGuards: the tommy skipped the best " +
                             "remaining guard.");
        }

        static void DeadReceiveNothing(List<string> failures)
        {
            var roster = new Roster();
            var corpse = Make(roster, "Late", "Fella", status: CharacterStatus.Dead);
            var lieutenant = Make(roster, "Head", "Crew", Rank.Lieutenant);
            var crew = MakeCrew(roster, lieutenant);
            var pistol = MakeItem(roster, EquipmentKind.Pistol);

            if (RosterOps.AssignToCrew(roster, corpse.Id, crew.Id).Ok ||
                RosterOps.AssignToFront(roster, corpse.Id).Ok ||
                RosterOps.GiveEquipment(roster, pistol.Id, corpse.Id).Ok)
                failures.Add("DeadReceiveNothing: the dead man got something.");
        }

        // --------------------------------------------------------------------- view

        static void ViewGroupsInLedgerOrder(List<string> failures)
        {
            var roster = RosterSeeder.Generate(13);
            var rows = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions(), rows);

            var expected = new[]
            {
                RowKind.CrewHeader, RowKind.Lieutenant, RowKind.Character, RowKind.Character,
                RowKind.FrontHeader, RowKind.Character,
                RowKind.PoolHeader, RowKind.Character, RowKind.Character,
            };

            if (rows.Count != expected.Length)
            {
                failures.Add($"ViewGroupsInLedgerOrder: {rows.Count} rows, " +
                             $"expected {expected.Length}.");
                return;
            }
            for (var i = 0; i < expected.Length; i++)
                if (rows[i].Kind != expected[i])
                    failures.Add($"ViewGroupsInLedgerOrder: row {i} is {rows[i].Kind}.");
        }

        static void ViewSortsWithinGroups(List<string> failures)
        {
            var roster = RosterSeeder.Generate(17);
            var rows = new List<LedgerRow>();
            var options = new ViewOptions
            {
                Sort = SortKey.Attribute,
                SortAttribute = CharacterAttribute.Firearms,
            };
            RosterView.Build(roster, options, rows);

            // Grouping must survive the sort untouched, the lieutenant pinned first.
            var kinds = new List<RowKind>();
            foreach (var row in rows)
                kinds.Add(row.Kind);
            if (kinds.Count != 9 || kinds[0] != RowKind.CrewHeader ||
                kinds[1] != RowKind.Lieutenant)
                failures.Add("ViewSortsWithinGroups: grouping did not survive the sort.");

            // Character runs between headers must descend, ties broken by ascending id.
            var previous = int.MaxValue;
            var previousId = -1;
            foreach (var row in rows)
            {
                if (row.Kind != RowKind.Character)
                {
                    previous = int.MaxValue;
                    previousId = -1;
                    continue;
                }

                var value = roster.Find(row.CharacterId)
                    .GetHalfSteps(CharacterAttribute.Firearms);
                if (value > previous ||
                    (value == previous && row.CharacterId < previousId))
                    failures.Add("ViewSortsWithinGroups: out of order within a group.");
                previous = value;
                previousId = row.CharacterId;
            }
        }

        static void ViewFiltersCompose(List<string> failures)
        {
            var roster = RosterSeeder.Generate(21);
            var pool = new List<int>();
            roster.PoolIds(pool);

            var rows = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions
            {
                Rank = RankFilter.Hoods,
                Assignment = AssignmentFilter.Pool,
            }, rows);

            if (rows.Count != 1 + pool.Count)
            {
                failures.Add($"ViewFiltersCompose: {rows.Count} rows for the pool view.");
                return;
            }
            if (rows[0].Kind != RowKind.PoolHeader)
                failures.Add("ViewFiltersCompose: no pool header first.");
            for (var i = 1; i < rows.Count; i++)
                if (!pool.Contains(rows[i].CharacterId))
                    failures.Add("ViewFiltersCompose: a non-pool man slipped through.");

            // A filter that matches nobody leaves no orphan headers behind.
            RosterView.Build(roster, new ViewOptions
            {
                Availability = AvailabilityFilter.Unavailable,
            }, rows);
            if (rows.Count != 0)
                failures.Add("ViewFiltersCompose: empty groups kept their headers.");
        }

        static void ViewScalesToSixty(List<string> failures)
        {
            var roster = new Roster();
            for (var i = 0; i < 60; i++)
                Make(roster, "Man", "Number" + i);

            // Six crews of four, one front, the rest pooled - built through the ops so
            // the fixture cannot disagree with the rules.
            for (var c = 0; c < 6; c++)
            {
                var lieutenantId = roster.Members[c * 5].Id;
                RosterOps.Promote(roster, lieutenantId, out var crewId);
                for (var h = 1; h <= 4; h++)
                    RosterOps.AssignToCrew(roster, roster.Members[c * 5 + h].Id, crewId);
            }
            RosterOps.AssignToFront(roster, roster.Members[30].Id);

            var rows = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions(), rows);

            // Every man exactly once: hoods/front/pool as Character rows, each
            // lieutenant as his own Lieutenant row under his crew's header.
            var seen = new HashSet<int>();
            var headers = 0;
            foreach (var row in rows)
            {
                if (row.Kind == RowKind.Character || row.Kind == RowKind.Lieutenant)
                {
                    if (!seen.Add(row.CharacterId))
                        failures.Add($"ViewScalesToSixty: id {row.CharacterId} listed twice.");
                }
                else if (row.Kind == RowKind.CrewHeader)
                    headers++;
            }

            if (headers != 6 || seen.Count != 60)
                failures.Add($"ViewScalesToSixty: {headers} crews / {seen.Count} men shown.");
        }

        static void LieutenantHasOwnRow(List<string> failures)
        {
            var roster = RosterSeeder.Generate(29);
            var rows = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions(), rows);

            if (rows.Count < 2 || rows[0].Kind != RowKind.CrewHeader ||
                rows[1].Kind != RowKind.Lieutenant)
            {
                failures.Add("LieutenantHasOwnRow: no Lieutenant row under the header.");
                return;
            }
            if (rows[1].CharacterId != roster.Crews[0].LieutenantId)
                failures.Add("LieutenantHasOwnRow: the row is not the crew's lieutenant.");

            // Filtered to lieutenants only, the crew shrinks to header + his row -
            // and the front/pool sections (all hoods) drop their headers entirely.
            RosterView.Build(roster, new ViewOptions { Rank = RankFilter.Lieutenants }, rows);
            if (rows.Count != 2 || rows[1].Kind != RowKind.Lieutenant)
                failures.Add($"LieutenantHasOwnRow: lieutenant filter shows {rows.Count} rows.");
        }

        static void LargeRosterShape(List<string> failures)
        {
            var a = RosterSeeder.GenerateLarge(42, 60);
            var b = RosterSeeder.GenerateLarge(42, 60);

            if (a.Members.Count != 60 || a.Crews.Count != 6)
            {
                failures.Add($"LargeRosterShape: {a.Members.Count} men / {a.Crews.Count} crews.");
                return;
            }

            foreach (var crew in a.Crews)
                if (crew.HoodIds.Count != 4)
                    failures.Add($"LargeRosterShape: crew {crew.Id} has {crew.HoodIds.Count} hoods.");

            if (a.FrontId < 0)
                failures.Add("LargeRosterShape: no front.");

            var pool = new List<int>();
            a.PoolIds(pool);
            if (pool.Count != 60 - 1 - 6 - 24 - 1)
                failures.Add($"LargeRosterShape: pool of {pool.Count}.");

            for (var i = 0; i < a.Members.Count; i++)
                if (a.Members[i].FullName != b.Members[i].FullName)
                    failures.Add("LargeRosterShape: same seed rolled different men.");
        }

        // ------------------------------------------------------------------ wording

        static void LedgerTextIsExhaustive(List<string> failures)
        {
            foreach (CharacterAttribute a in Enum.GetValues(typeof(CharacterAttribute)))
                if (LedgerText.AttributeLabel(a).Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: attribute {a} has no label.");
            foreach (Rank r in Enum.GetValues(typeof(Rank)))
                if (LedgerText.RankLabel(r).Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: rank {r} has no label.");
            foreach (CharacterStatus s in Enum.GetValues(typeof(CharacterStatus)))
                if (LedgerText.StatusLabel(s).Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: status {s} has no label.");
            foreach (EquipmentKind k in Enum.GetValues(typeof(EquipmentKind)))
                if (LedgerText.EquipmentLabel(k).Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: kind {k} has no label.");
            foreach (Specialty s in Enum.GetValues(typeof(Specialty)))
                if (s != Specialty.None && LedgerText.SpecialtyLabel(s).Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: specialty {s} has no label.");

            for (var half = AttributeScale.MinHalfSteps;
                 half <= AttributeScale.MaxHalfSteps; half++)
            {
                var text = LedgerText.Stars(half);
                if (text.Length == 0)
                    failures.Add($"LedgerTextIsExhaustive: Stars({half}) is empty.");
                if (half % 2 == 1 && !text.EndsWith(".5"))
                    failures.Add($"LedgerTextIsExhaustive: Stars({half}) lost its half.");
            }

            if (LedgerText.PromoteWarning("X").Length == 0 ||
                LedgerText.DemoteConfirm("X", 0).Length == 0 ||
                LedgerText.DemoteConfirm("X", 1).Length == 0 ||
                LedgerText.DemoteConfirm("X", 3).Length == 0 ||
                LedgerText.HeldByLine("X").Length == 0 ||
                LedgerText.MemberCount(1).Length == 0)
                failures.Add("LedgerTextIsExhaustive: a composed line came back empty.");
        }

        static void LabelsFitTheLedgerColumns(List<string> failures)
        {
            // The detail card's label cell is 160px at 14pt; 13 characters is the proven
            // fit ("Intelligence" and "Organization" at 12 set the budget).
            foreach (CharacterAttribute a in Enum.GetValues(typeof(CharacterAttribute)))
                if (LedgerText.AttributeLabel(a).Length > 13)
                    failures.Add($"LabelsFitTheLedgerColumns: {a} overflows the label cell.");

            // The roster row's status stamp cell is 120px at 12pt bold caps: 12 chars.
            foreach (CharacterStatus s in Enum.GetValues(typeof(CharacterStatus)))
                if (LedgerText.StatusLabel(s).Length > 12)
                    failures.Add($"LabelsFitTheLedgerColumns: {s} overflows the stamp cell.");
        }
    }
}
