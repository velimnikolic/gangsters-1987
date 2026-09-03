using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Property;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 27, the flats: how a building deals its units (FLAT-001), the roles and the
    /// one-man-one-flat keeper rules (FLAT-002), and what a flat READS as on any given day
    /// (the sheet's own question).
    ///
    /// Plain static class, failures as data, no UnityEngine in the parts that matter - the
    /// book is pure, which is what lets the rules be assertions rather than beliefs. The
    /// half that needs a dealt city (the buildings themselves) is checked by
    /// <c>gangsters_flat_tests</c>, which deals a quarter first and hands the count in.
    /// </summary>
    public static class FlatTests
    {
        static readonly (string Name, System.Action<List<string>> Check)[] Contracts =
        {
            ("ADoorIsNamedByItsFloorAndItsLetter", ADoorIsNamedByItsFloorAndItsLetter),
            ("ALandingRunsPastTheTenthLetter", ALandingRunsPastTheTenthLetter),
            ("ABoughtFlatIsOursAndNothingElse", ABoughtFlatIsOursAndNothingElse),
            ("NoKeeperNoEffect", NoKeeperNoEffect),
            ("AKeeperInACellLeavesItDark", AKeeperInACellLeavesItDark),
            ("OneManKeepsOneFlat", OneManKeepsOneFlat),
            ("ACardRoomWithNoBankIsShut", ACardRoomWithNoBankIsShut),
            ("ARaidSealsItUntilAnAbsoluteDay", ARaidSealsItUntilAnAbsoluteDay),
            ("OnlyTheCardRoomAndTheBrothelTakeAnything",
                OnlyTheCardRoomAndTheBrothelTakeAnything),
            ("AKeeperComesOffTheStreet", AKeeperComesOffTheStreet),
            ("TheBossAndALieutenantAreNotSpared", TheBossAndALieutenantAreNotSpared),
            ("RentIsOwedOnEveryRoomOpenOrDark", RentIsOwedOnEveryRoomOpenOrDark),
            ("ABrothelWithNoGirlsTakesNothing", ABrothelWithNoGirlsTakesNothing),
            ("AnOpenRoomPutsHeatOnItsBlock", AnOpenRoomPutsHeatOnItsBlock),
            ("ADeadKeeperLosesTheRoom", ADeadKeeperLosesTheRoom),
        };

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
            {
                Apartments.Clear();
                Contracts[i].Check(failures);
            }
            Apartments.Clear();
            return failures;
        }

        // ------------------------------------------------------------------ FLAT-001

        static void ADoorIsNamedByItsFloorAndItsLetter(List<string> failures)
        {
            var unit = Unit(3, 2);
            if (unit.Door != "3C")
                failures.Add("ADoorIsNamedByItsFloorAndItsLetter: the third door of the " +
                             "third floor reads '" + unit.Door + "', not 3C.");
        }

        /// <summary>A landing runs from one door to twenty-two on the measured fabric, so
        /// the letters cannot stop at J the way the design prototype's fixed grid did.</summary>
        static void ALandingRunsPastTheTenthLetter(List<string> failures)
        {
            if (ApartmentBuildings.DoorLetter(0) != "A" ||
                ApartmentBuildings.DoorLetter(9) != "J" ||
                ApartmentBuildings.DoorLetter(21) != "V")
                failures.Add("ALandingRunsPastTheTenthLetter: the letters read " +
                             ApartmentBuildings.DoorLetter(0) + "/" +
                             ApartmentBuildings.DoorLetter(9) + "/" +
                             ApartmentBuildings.DoorLetter(21) + ", not A/J/V.");
            if (ApartmentBuildings.DoorLetter(26) != "AA")
                failures.Add("ALandingRunsPastTheTenthLetter: the twenty-seventh door " +
                             "reads " + ApartmentBuildings.DoorLetter(26) + ", not AA.");
        }

        static void ABoughtFlatIsOursAndNothingElse(List<string> failures)
        {
            var unit = Unit(2, 0);
            Apartments.Buy(unit, 0, 12);

            if (Apartments.OwnerOf(unit) != 0)
                failures.Add("ABoughtFlatIsOursAndNothingElse: the deed did not move.");
            if (Apartments.StateOf(unit, 0, 12, true) != UnitState.Dark)
                failures.Add("ABoughtFlatIsOursAndNothingElse: a flat with no role reads " +
                             Apartments.StateOf(unit, 0, 12, true) + ", not DARK.");
            if (Apartments.StateOf(Unit(2, 1), 0, 12, true) != UnitState.NotOurs)
                failures.Add("ABoughtFlatIsOursAndNothingElse: buying one door bought " +
                             "the one beside it.");
        }

        // ------------------------------------------------------------------ FLAT-002

        static void NoKeeperNoEffect(List<string> failures)
        {
            var unit = Unit(1, 0);
            Apartments.Buy(unit, 0, 1);
            Apartments.SetRole(unit, UnitRole.CashStash, true);

            if (Apartments.StateOf(unit, 0, 1, true) != UnitState.Dark)
                failures.Add("NoKeeperNoEffect: a stash with no keeper reads " +
                             Apartments.StateOf(unit, 0, 1, true) + ", not DARK.");

            Apartments.SetKeeper(unit, 7);
            if (Apartments.StateOf(unit, 0, 1, true) != UnitState.Open)
                failures.Add("NoKeeperNoEffect: a stash with a role and a keeper is not " +
                             "open.");
        }

        static void AKeeperInACellLeavesItDark(List<string> failures)
        {
            var unit = Unit(1, 1);
            Apartments.Buy(unit, 0, 1);
            Apartments.SetRole(unit, UnitRole.Armory, true);
            Apartments.SetKeeper(unit, 9);

            // The mark stays his; the work does not happen.
            if (Apartments.StateOf(unit, 0, 1, false) != UnitState.Dark)
                failures.Add("AKeeperInACellLeavesItDark: a keeper who is not standing " +
                             "in the room still opens it.");
            if (Apartments.KeptBy(9) != unit)
                failures.Add("AKeeperInACellLeavesItDark: the flat forgot whose it is " +
                             "while he was away.");
        }

        static void OneManKeepsOneFlat(List<string> failures)
        {
            var first = Unit(1, 0);
            var second = Unit(2, 0);
            Apartments.Buy(first, 0, 1);
            Apartments.Buy(second, 0, 1);
            Apartments.SetRole(first, UnitRole.Armory, true);
            Apartments.SetRole(second, UnitRole.Safehouse, true);

            Apartments.SetKeeper(first, 4);
            Apartments.SetKeeper(second, 4);

            if (Apartments.TryGet(first, out var one) && one.KeeperId == 4)
                failures.Add("OneManKeepsOneFlat: one man is keeping two rooms.");
            if (!Apartments.TryGet(second, out var two) || two.KeeperId != 4)
                failures.Add("OneManKeepsOneFlat: he did not take the second room.");
        }

        static void ACardRoomWithNoBankIsShut(List<string> failures)
        {
            var unit = Unit(4, 3);
            Apartments.Buy(unit, 0, 5);
            Apartments.SetRole(unit, UnitRole.CardRoom, true);
            Apartments.SetKeeper(unit, 3);

            if (Apartments.StateOf(unit, 0, 5, true) != UnitState.NoBank)
                failures.Add("ACardRoomWithNoBankIsShut: a table with no money behind " +
                             "it reads " + Apartments.StateOf(unit, 0, 5, true) + ".");

            Apartments.SetBank(unit, 2_000);
            if (Apartments.StateOf(unit, 0, 5, true) != UnitState.Open)
                failures.Add("ACardRoomWithNoBankIsShut: money went behind the table and " +
                             "the room stayed shut.");
        }

        static void ARaidSealsItUntilAnAbsoluteDay(List<string> failures)
        {
            var unit = Unit(5, 1);
            Apartments.Buy(unit, 0, 100);
            Apartments.SetRole(unit, UnitRole.Brothel, true);
            Apartments.SetKeeper(unit, 2);
            Apartments.Raid(unit, 214);

            if (Apartments.StateOf(unit, 0, 200, true) != UnitState.Raided)
                failures.Add("ARaidSealsItUntilAnAbsoluteDay: the seal is not holding on " +
                             "day 200.");
            if (Apartments.StateOf(unit, 0, 214, true) != UnitState.Open)
                failures.Add("ARaidSealsItUntilAnAbsoluteDay: the seal outlived its own " +
                             "day - a countdown, not an absolute day.");
            if (Apartments.TryGet(unit, out var record) && record.Bank != 0)
                failures.Add("ARaidSealsItUntilAnAbsoluteDay: the precinct left the bank " +
                             "on the table.");
        }

        /// <summary>No income from a flat except the card room and the brothel. The rest
        /// hold goods or people - and the day the garage starts taking a nightly cut, this
        /// is the assertion that says so out loud.</summary>
        static void OnlyTheCardRoomAndTheBrothelTakeAnything(List<string> failures)
        {
            for (var i = 0; i < UnitRoles.All.Length; i++)
            {
                var spec = UnitRoles.All[i];
                var earns = spec.Role == UnitRole.CardRoom || spec.Role == UnitRole.Brothel;
                if (earns == spec.Earn > 0)
                    continue;
                failures.Add("OnlyTheCardRoomAndTheBrothelTakeAnything: " + spec.Label +
                             " takes " + spec.Earn + " a day.");
            }
        }

        // ---------------------------------------------------------- the keeper's rules

        static void AKeeperComesOffTheStreet(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = Hire(roster, Rank.Lieutenant);
            var hood = Hire(roster, Rank.Hood);
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            roster.Crews.Add(crew);
            crew.HoodIds.Add(hood.Id);

            var result = RosterOps.SetKeeper(roster, hood.Id);
            if (!result.Ok)
                failures.Add("AKeeperComesOffTheStreet: a hood in a crew was refused: " +
                             result.Reason);
            if (hood.Duty != Duty.Keeper)
                failures.Add("AKeeperComesOffTheStreet: he is not marked as keeping it.");
            if (crew.HoodIds.Contains(hood.Id))
                failures.Add("AKeeperComesOffTheStreet: he is standing in the room AND " +
                             "walking with his crew.");

            RosterOps.ClearKeeper(roster, hood.Id);
            if (hood.Duty != Duty.None)
                failures.Add("AKeeperComesOffTheStreet: pulling him out left the mark on " +
                             "him.");
        }

        static void TheBossAndALieutenantAreNotSpared(List<string> failures)
        {
            var roster = new Roster();
            var boss = Hire(roster, Rank.Boss);
            roster.Organization.BossId = boss.Id;
            var lieutenant = Hire(roster, Rank.Lieutenant);
            var hurt = Hire(roster, Rank.Hood);
            hurt.Status = CharacterStatus.Hospitalized;

            if (RosterOps.CanKeep(roster, boss.Id, out _))
                failures.Add("TheBossAndALieutenantAreNotSpared: the boss was put in a " +
                             "room.");
            if (RosterOps.CanKeep(roster, lieutenant.Id, out _))
                failures.Add("TheBossAndALieutenantAreNotSpared: a lieutenant left his " +
                             "branch to mind a flat.");
            if (RosterOps.CanKeep(roster, hurt.Id, out var why))
                failures.Add("TheBossAndALieutenantAreNotSpared: a man in a bed is " +
                             "standing in a room.");
            else if (why != "hurt")
                failures.Add("TheBossAndALieutenantAreNotSpared: the refusal reads '" +
                             why + "', which does not say he is hurt.");
        }

        // ------------------------------------------------------------ FLAT-003/004

        static void RentIsOwedOnEveryRoomOpenOrDark(List<string> failures)
        {
            var roster = new Roster();
            var accounts = new LivingCity.Outfit.Accounts();
            accounts.Open(1);
            Apartments.Buy(Unit(1, 0), 0, 1);
            Apartments.Buy(Unit(1, 1), 0, 1);

            var report = new FlatDayReport();
            FlatDay.Tick(roster, 0, 2, 1987, accounts, new List<Incident>(), report);

            if (report.Rent != FlatDay.RentPerDay * 2)
                failures.Add("RentIsOwedOnEveryRoomOpenOrDark: two dark rooms cost " +
                             report.Rent + ", not two days' rent.");
            if (accounts.Current == null ||
                accounts.Current.OtherCosts != FlatDay.RentPerDay * 2)
                failures.Add("RentIsOwedOnEveryRoomOpenOrDark: the rent never reached the " +
                             "day's sheet.");
        }

        static void ABrothelWithNoGirlsTakesNothing(List<string> failures)
        {
            var roster = new Roster();
            var keeper = Hire(roster, Rank.Hood);
            var accounts = new LivingCity.Outfit.Accounts();
            accounts.Open(1);

            var unit = Unit(3, 0);
            Apartments.Buy(unit, 0, 1);
            Apartments.SetRole(unit, UnitRole.Brothel, true);
            Apartments.SetKeeper(unit, keeper.Id);

            var report = new FlatDayReport();
            FlatDay.Tick(roster, 0, 2, 1987, accounts, new List<Incident>(), report);
            if (report.IllegalIncome != 0)
                failures.Add("ABrothelWithNoGirlsTakesNothing: an empty room took " +
                             report.IllegalIncome + " overnight.");

            Apartments.SetStaff(unit, 2);
            report = new FlatDayReport();
            FlatDay.Tick(roster, 0, 3, 1987, accounts, new List<Incident>(), report);
            if (report.IllegalIncome <= 0)
                failures.Add("ABrothelWithNoGirlsTakesNothing: two girls worked and the " +
                             "house took nothing.");
            if (report.StaffWages != UnitRoles.BrothelWagePerGirl * 2)
                failures.Add("ABrothelWithNoGirlsTakesNothing: the girls were paid " +
                             report.StaffWages + ".");
        }

        static void AnOpenRoomPutsHeatOnItsBlock(List<string> failures)
        {
            var roster = new Roster();
            var keeper = Hire(roster, Rank.Hood);
            var accounts = new LivingCity.Outfit.Accounts();
            accounts.Open(1);

            var unit = Unit(2, 2);
            Apartments.Buy(unit, 0, 1);
            Apartments.SetRole(unit, UnitRole.CardRoom, true);
            Apartments.SetKeeper(unit, keeper.Id);
            Apartments.SetBank(unit, 5_000);

            var report = new FlatDayReport();
            FlatDay.Tick(roster, 0, 2, 1987, accounts, new List<Incident>(), report);

            if (report.Heat.Count != 1)
                failures.Add("AnOpenRoomPutsHeatOnItsBlock: an open card room asked for " +
                             report.Heat.Count + " deposits of heat.");
            else if (report.Heat[0].Heat <= UnitRoles.Of(UnitRole.CardRoom).Heat)
                failures.Add("AnOpenRoomPutsHeatOnItsBlock: the deposit was not scaled " +
                             "against the block's decaying pool - it would never show.");
        }

        static void ADeadKeeperLosesTheRoom(List<string> failures)
        {
            var roster = new Roster();
            var keeper = Hire(roster, Rank.Hood);
            var accounts = new LivingCity.Outfit.Accounts();
            accounts.Open(1);

            var unit = Unit(4, 0);
            Apartments.Buy(unit, 0, 1);
            Apartments.SetRole(unit, UnitRole.Armory, true);
            Apartments.SetKeeper(unit, keeper.Id);
            keeper.Status = CharacterStatus.Dead;

            var report = new FlatDayReport();
            FlatDay.Tick(roster, 0, 2, 1987, accounts, new List<Incident>(), report);

            if (Apartments.TryGet(unit, out var record) && record.KeeperId >= 0)
                failures.Add("ADeadKeeperLosesTheRoom: a dead man is still down as " +
                             "keeping it.");
            if (report.Open != 0)
                failures.Add("ADeadKeeperLosesTheRoom: the room counted as open with " +
                             "nobody alive in it.");
        }

        // ------------------------------------------------------------------ the fixture

        static ApartmentUnitId Unit(int floor, int slot) =>
            new ApartmentUnitId(
                new ApartmentBuildingId("flat|test|spot:0:residential-01"), floor, slot);

        static Character Hire(Roster roster, Rank rank)
        {
            var member = new Character
            {
                Id = roster.NextCharacterId(),
                FirstName = "MAN",
                Surname = "N" + roster.Members.Count,
                Rank = rank,
            };
            roster.Members.Add(member);
            return member;
        }
    }
}
