using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 15's contract: loyalty is owed to a MAN, not to an organization, and
    /// betrayal is one arithmetic rather than a scripted event.
    ///
    /// What the suite is really defending is that the player can always see it coming.
    /// A lieutenant walks because his loyalty ran out; his loyalty ran out because of
    /// named things that were printed as they happened; and the number of men he takes
    /// with him is the Leadership the player promoted him for. Nothing in the chain is
    /// a die roll, and every link prints.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class LoyaltyTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("TheSuperiorIsDerivedNeverStored", TheSuperiorIsDerivedNeverStored),
            ("ATransferStartsANewRelationship", ATransferStartsANewRelationship),
            ("TheParkedAmbitiousManBleeds", TheParkedAmbitiousManBleeds),
            ("TheSettledManHoldsBesideHim", TheSettledManHoldsBesideHim),
            ("ACrowdedCrewCostsTheMenInIt", ACrowdedCrewCostsTheMenInIt),
            ("EveryLoyaltyMovementCarriesAReason", EveryLoyaltyMovementCarriesAReason),
            ("CrossingTheWatchBandIsPrintedOnce", CrossingTheWatchBandIsPrintedOnce),
            ("HeWalksOnTheDayTheNumbersSayHeDoes", HeWalksOnTheDayTheNumbersSayHeDoes),
            ("HowManyHeTakesIsHisLeadership", HowManyHeTakesIsHisLeadership),
            ("HeTakesOnlyHisOwnAndOnlyTheLoyal", HeTakesOnlyHisOwnAndOnlyTheLoyal),
            ("NothingIsLeftDanglingAfterHeGoes", NothingIsLeftDanglingAfterHeGoes),
            ("TheBookSaysHowManyWentWithHim", TheBookSaysHowManyWentWithHim),
            ("ALoyalOutfitNeverLosesAMan", ALoyalOutfitNeverLosesAMan),
            ("PromotionChangesRankAndNothingElse", PromotionChangesRankAndNothingElse),
            ("PromotionIsRefusedPastTheBossesSpan", PromotionIsRefusedPastTheBossesSpan),
            ("ASpecialistIsNeverPromoted", ASpecialistIsNeverPromoted),
            ("TheOldCrewFeelsHimRise", TheOldCrewFeelsHimRise),
            ("ADemotionIsAllowedAndBrutal", ADemotionIsAllowedAndBrutal),
            ("TheThreeFlagsAnswerAtTheirThresholds", TheThreeFlagsAnswerAtTheirThresholds),
            ("TheRoundingIsStatedAtTheBoundary", TheRoundingIsStatedAtTheBoundary),
            ("CrossingIntoAFlagIsNewsExactlyOnce", CrossingIntoAFlagIsNewsExactlyOnce),
            ("AFlagNeverActsByItself", AFlagNeverActsByItself),
            ("ADefectorHasSomewhereToGo", ADefectorHasSomewhereToGo),
            ("TheSameCityOpensTheSameDoor", TheSameCityOpensTheSameDoor),
            ("TheDayIsFoldedOneLineAMan", TheDayIsFoldedOneLineAMan),
            ("EveryReasonReachesTheBook", EveryReasonReachesTheBook),
            ("TimeInRankIsWhatTheModelReads", TimeInRankIsWhatTheModelReads),
            ("EveryHouseWalksOutOfItsOwnCity", EveryHouseWalksOutOfItsOwnCity),
            ("SouringNeverSoftensAnOrderHeGave", SouringNeverSoftensAnOrderHeGave),
            ("WhatAHouseTookIsNeverForgotten", WhatAHouseTookIsNeverForgotten),
            ("TheFeedReadsLastNightLoudestFirst", TheFeedReadsLastNightLoudestFirst),
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        // ------------------------------------------------------------- the fixtures

        /// <summary>A Boss with nothing under him yet, and a span wide enough that the
        /// span rule is never what a test trips over unless it means to.</summary>
        static Roster BareOutfit()
        {
            var roster = new Roster();
            var boss = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Don", Surname = "Ricci",
                Rank = Rank.Boss, Loyalty = 100,
            };
            for (var a = 0; a < AttributeScale.Count; a++)
                boss.SetHalfSteps((CharacterAttribute)a, AttributeScale.MaxHalfSteps);
            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;
            RosterOps.ConfigureOrganization(roster, OrganizationLimits.Default);
            return roster;
        }

        static Character AddHood(Roster roster, string surname, int loyalty = 50)
        {
            var hood = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Man", Surname = surname,
                Loyalty = loyalty,
            };
            roster.Members.Add(hood);
            RosterOps.AssignToBoss(roster, hood.Id, roster.BossId);
            // The assignment itself re-aims him; the fixture wants the number it asked
            // for, so it is written back afterwards.
            hood.Loyalty = loyalty;
            return hood;
        }

        /// <summary>A lieutenant with a crew of n hoods, all of them at one loyalty.</summary>
        static Character AddCrew(Roster roster, string surname, int leadershipHalfSteps,
            int hoods, int hoodLoyalty, out Crew crew)
        {
            var lieutenant = AddHood(roster, surname);
            lieutenant.SetHalfSteps(CharacterAttribute.Leadership, leadershipHalfSteps);
            RosterOps.Promote(roster, lieutenant.Id, out var crewId);
            crew = roster.FindCrew(crewId);

            for (var i = 0; i < hoods; i++)
            {
                var hood = AddHood(roster, surname + "Man" + i);
                RosterOps.AssignToCrew(roster, hood.Id, crewId);
                hood.Loyalty = hoodLoyalty;
            }
            return lieutenant;
        }

        // ------------------------------------------------------- LOY-001, the aim

        static void TheSuperiorIsDerivedNeverStored(List<string> failures)
        {
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Bruno", 8, 2, 60, out var crew);
            var hood = roster.Find(crew.HoodIds[0]);
            var underBoss = AddHood(roster, "Direct");

            var branch = new OrganizationQuery(roster);

            if (!branch.TryGetCommandParent(hood.Id, out var above) ||
                above.Id != lieutenant.Id)
                failures.Add("TheSuperiorIsDerivedNeverStored: a hood in a crew does " +
                             "not answer to his lieutenant.");

            if (!branch.TryGetCommandParent(lieutenant.Id, out var overHim) ||
                overHim.Id != roster.BossId)
                failures.Add("TheSuperiorIsDerivedNeverStored: a lieutenant does not " +
                             "answer to the Boss.");

            if (!branch.TryGetCommandParent(underBoss.Id, out var direct) ||
                direct.Id != roster.BossId)
                failures.Add("TheSuperiorIsDerivedNeverStored: a hood standing directly " +
                             "under the Boss does not answer to him.");

            if (branch.TryGetCommandParent(roster.BossId, out _))
                failures.Add("TheSuperiorIsDerivedNeverStored: the Boss answers to " +
                             "somebody.");
        }

        static void ATransferStartsANewRelationship(List<string> failures)
        {
            var roster = BareOutfit();
            AddCrew(roster, "Bruno", 8, 1, 50, out var first);
            AddCrew(roster, "Costa", 8, 0, 50, out var second);

            var hood = roster.Find(first.HoodIds[0]);
            Personality.Set(hood, PersonalityTrait.Discipline, 50);
            hood.Loyalty = 95;

            RosterOps.AssignToCrew(roster, hood.Id, second.Id);
            if (hood.Loyalty != Loyalty.Neutral)
                failures.Add("ATransferStartsANewRelationship: a devoted man carried " +
                             $"{hood.Loyalty} to his new lieutenant, not " +
                             $"{Loyalty.Neutral}.");

            // And the only thing that survives the move is his own steadiness.
            var wild = AddHood(roster, "Wild");
            Personality.Set(wild, PersonalityTrait.Discipline, 0);
            wild.Loyalty = 95;
            RosterOps.AssignToCrew(roster, wild.Id, second.Id);

            var exact = AddHood(roster, "Exact");
            Personality.Set(exact, PersonalityTrait.Discipline, 100);
            exact.Loyalty = 5;
            RosterOps.AssignToCrew(roster, exact.Id, second.Id);

            if (wild.Loyalty >= exact.Loyalty)
                failures.Add("ATransferStartsANewRelationship: discipline buys no " +
                             $"benefit of the doubt - wild {wild.Loyalty}, exact " +
                             $"{exact.Loyalty}.");
        }

        static void TheParkedAmbitiousManBleeds(List<string> failures)
        {
            var man = new Character { Id = 1, FirstName = "Man", Surname = "Hungry" };
            Personality.Set(man, PersonalityTrait.Ambition, 80);
            man.Loyalty = 70;

            var changes = new List<PersonalityChange>();
            var incidents = new List<Incident>();

            // Ten weeks parked, paid the rate throughout: the parked loss is the bigger
            // of the two, so he goes down.
            for (var day = 1; day <= 70; day++)
                Loyalty.Drift(man, true, false, 0, day,
                    day + Loyalty.ParkedDays + 1, changes, incidents);

            if (man.Loyalty >= 70)
                failures.Add($"TheParkedAmbitiousManBleeds: he sat at {man.Loyalty}.");

            var weeks = 70 / Loyalty.DriftEveryDays;
            var expected = 70 + weeks * (Loyalty.PaidOnTimeGain - Loyalty.ParkedLoss);
            if (man.Loyalty != expected)
                failures.Add($"TheParkedAmbitiousManBleeds: {man.Loyalty} after ten " +
                             $"weeks, not the scheduled {expected}.");
        }

        static void TheSettledManHoldsBesideHim(List<string> failures)
        {
            var man = new Character { Id = 1, FirstName = "Man", Surname = "Settled" };
            Personality.Set(man, PersonalityTrait.Ambition, 20);
            man.Loyalty = 70;

            var changes = new List<PersonalityChange>();
            for (var day = 1; day <= 70; day++)
                Loyalty.Drift(man, true, false, 0, day,
                    day + Loyalty.ParkedDays + 1, changes, null);

            if (man.Loyalty < 70)
                failures.Add($"TheSettledManHoldsBesideHim: he fell to {man.Loyalty} " +
                             "with no ambition to be disappointed.");
        }

        static void ACrowdedCrewCostsTheMenInIt(List<string> failures)
        {
            var crowded = new Character { Id = 1, FirstName = "A", Surname = "Crowded" };
            var roomy = new Character { Id = 2, FirstName = "B", Surname = "Roomy" };
            foreach (var man in new[] { crowded, roomy })
            {
                Personality.Set(man, PersonalityTrait.Ambition, 20);
                man.Loyalty = 60;
            }

            for (var day = 1; day <= 28; day++)
            {
                // Underpaid, so the "paid the rate" gain cannot mask the crowd's cost.
                Loyalty.Drift(crowded, true, true, 5, day, 1, null, null);
                Loyalty.Drift(roomy, true, false, 5, day, 1, null, null);
            }

            if (crowded.Loyalty >= roomy.Loyalty)
                failures.Add("ACrowdedCrewCostsTheMenInIt: being one man too many cost " +
                             $"nothing - crowded {crowded.Loyalty}, roomy " +
                             $"{roomy.Loyalty}.");
        }

        static void EveryLoyaltyMovementCarriesAReason(List<string> failures)
        {
            var changes = new List<PersonalityChange>();

            var man = new Character { Id = 1, FirstName = "Man", Surname = "Reason" };
            Personality.Set(man, PersonalityTrait.Ambition, 90);
            Personality.Set(man, PersonalityTrait.Discipline, 40);
            man.Loyalty = 60;

            for (var day = 1; day <= 28; day++)
                Loyalty.Drift(man, true, true, 9, day, 999, changes, null);
            Loyalty.Reaim(man, "moved", changes);
            Loyalty.Sting(man, changes);

            if (changes.Count == 0)
                failures.Add("EveryLoyaltyMovementCarriesAReason: nothing was recorded " +
                             "at all.");
            for (var i = 0; i < changes.Count; i++)
            {
                if (changes[i].Reason.Length == 0)
                    failures.Add("EveryLoyaltyMovementCarriesAReason: a movement of " +
                                 $"{changes[i].Delta} was recorded with no reason.");
                if (changes[i].Delta == 0)
                    failures.Add("EveryLoyaltyMovementCarriesAReason: a movement of " +
                                 "nothing was recorded.");
            }
        }

        static void CrossingTheWatchBandIsPrintedOnce(List<string> failures)
        {
            var man = new Character { Id = 1, FirstName = "Man", Surname = "Watched" };
            Personality.Set(man, PersonalityTrait.Ambition, 90);
            man.Loyalty = Loyalty.WatchBand + 1;

            var incidents = new List<Incident>();
            for (var day = 1; day <= 140; day++)
                Loyalty.Drift(man, true, true, 9, day, 999, null, incidents);

            var printed = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.BearsWatching)
                    printed++;

            if (printed != 1)
                failures.Add($"CrossingTheWatchBandIsPrintedOnce: printed {printed} " +
                             "times, not once.");
        }

        // -------------------------------------------------- LOY-002, the departure

        static void HeWalksOnTheDayTheNumbersSayHeDoes(List<string> failures)
        {
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Bruno", 8, 3, 80, out _);
            lieutenant.Loyalty = Defection.BreakingPoint + 1;

            var incidents = new List<Incident>();
            var before = Defection.Tick(roster, lieutenant, 10, incidents);
            if (before.Happened)
                failures.Add("HeWalksOnTheDayTheNumbersSayHeDoes: he went one point " +
                             "over the line.");

            lieutenant.Loyalty = Defection.BreakingPoint;
            var after = Defection.Tick(roster, lieutenant, 11, incidents);
            if (!after.Happened)
                failures.Add("HeWalksOnTheDayTheNumbersSayHeDoes: he stayed at the " +
                             "breaking point.");

            var printed = false;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Defected)
                    printed = true;
            if (!printed)
                failures.Add("HeWalksOnTheDayTheNumbersSayHeDoes: the paper carried " +
                             "nothing.");

            // And the same history twice takes the same day and the same men.
            var replayRoster = BareOutfit();
            var replay = AddCrew(replayRoster, "Bruno", 8, 3, 80, out _);
            replay.Loyalty = Defection.BreakingPoint;
            var second = Defection.Tick(replayRoster, replay, 11, null);
            if (second.TookWithHim.Length != after.TookWithHim.Length)
                failures.Add("HeWalksOnTheDayTheNumbersSayHeDoes: the same history " +
                             $"took {after.TookWithHim.Length} men, then " +
                             $"{second.TookWithHim.Length}.");
        }

        static void HowManyHeTakesIsHisLeadership(List<string> failures)
        {
            var taken = new int[AttributeScale.MaxHalfSteps + 1];
            for (var reach = AttributeScale.MinHalfSteps;
                 reach <= AttributeScale.MaxHalfSteps; reach++)
            {
                var roster = BareOutfit();
                var lieutenant = AddCrew(roster, "L" + reach, reach, 10, 90, out _);
                lieutenant.Loyalty = Defection.BreakingPoint;
                taken[reach] = Defection.Tick(roster, lieutenant, 5, null)
                    .TookWithHim.Length;
            }

            for (var reach = AttributeScale.MinHalfSteps + 1;
                 reach <= AttributeScale.MaxHalfSteps; reach++)
                if (taken[reach] < taken[reach - 1])
                    failures.Add($"HowManyHeTakesIsHisLeadership: {reach} half-steps " +
                                 $"took {taken[reach]}, fewer than {reach - 1}'s " +
                                 $"{taken[reach - 1]}.");

            if (taken[AttributeScale.MinHalfSteps] != 0)
                failures.Add("HowManyHeTakesIsHisLeadership: a man nobody would follow " +
                             $"still took {taken[AttributeScale.MinHalfSteps]} out.");
            if (taken[AttributeScale.MaxHalfSteps] <= taken[AttributeScale.MinHalfSteps])
                failures.Add("HowManyHeTakesIsHisLeadership: the best commander took no " +
                             "more than the worst.");
        }

        static void HeTakesOnlyHisOwnAndOnlyTheLoyal(List<string> failures)
        {
            var roster = BareOutfit();
            var his = AddCrew(roster, "Bruno", AttributeScale.MaxHalfSteps, 4, 90,
                out var mine);
            AddCrew(roster, "Costa", AttributeScale.MaxHalfSteps, 4, 90, out var theirs);

            // One of his own would not follow him anywhere.
            var reluctant = roster.Find(mine.HoodIds[0]);
            reluctant.Loyalty = Defection.FollowsAt - 1;

            his.Loyalty = Defection.BreakingPoint;
            var report = Defection.Tick(roster, his, 9, null);

            for (var i = 0; i < report.TookWithHim.Length; i++)
            {
                var id = report.TookWithHim[i];
                if (id == reluctant.Id)
                    failures.Add("HeTakesOnlyHisOwnAndOnlyTheLoyal: a man who would " +
                                 "not follow him was taken anyway.");
                if (theirs.HoodIds.Contains(id))
                    failures.Add("HeTakesOnlyHisOwnAndOnlyTheLoyal: he took another " +
                                 "crew's man.");
            }

            if (reluctant.Gone)
                failures.Add("HeTakesOnlyHisOwnAndOnlyTheLoyal: the man who stayed was " +
                             "struck off with the rest.");
        }

        static void NothingIsLeftDanglingAfterHeGoes(List<string> failures)
        {
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Bruno", AttributeScale.MaxHalfSteps, 4, 90,
                out _);
            for (var i = 0; i < 3; i++)
                RosterOps.AddEquipment(roster, EquipmentKind.Pistol, "Colt", 200);
            RosterOps.NormalizeArms(roster);

            lieutenant.Loyalty = Defection.BreakingPoint;
            var report = Defection.Tick(roster, lieutenant, 12, null);

            // Every man who went is off the books, and off every list on it.
            for (var i = 0; i < report.TookWithHim.Length; i++)
            {
                var gone = roster.Find(report.TookWithHim[i]);
                if (gone == null || !gone.Gone)
                {
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: a man who walked is " +
                                 "still on his feet.");
                    continue;
                }
                if (roster.CrewOf(gone.Id) != null)
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: " + gone.FullName +
                                 " is still in a crew.");
                if (roster.Organization.BossHoodIds.Contains(gone.Id))
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: " + gone.FullName +
                                 " still stands under the Boss.");
                if (roster.HeldCount(gone.Id) != 0)
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: " + gone.FullName +
                                 " walked off with the outfit's gear.");
                if (Wages.WageFor(gone) != 0)
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: " + gone.FullName +
                                 " is still drawing a wage.");
            }

            // And no item points at a man who is not there.
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var holder = roster.Equipment[i].HolderId;
                if (holder == RosterEquipment.Unheld)
                    continue;
                var man = roster.Find(holder);
                if (man == null || man.Gone)
                    failures.Add("NothingIsLeftDanglingAfterHeGoes: an item is held by " +
                                 "a man who is off the books.");
            }
        }

        /// <summary>
        /// A crew evaporating off the roll has to be READABLE. The paper carries the
        /// count, the lieutenant's own file says he went over and how many he carried,
        /// and every man who followed him carries a line saying he followed rather than
        /// the clerk's stock sentence about a runner - because the desertion door is
        /// the right door for the mechanics and the wrong words for the story.
        /// </summary>
        static void TheBookSaysHowManyWentWithHim(List<string> failures)
        {
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Bruno", AttributeScale.MaxHalfSteps, 4, 90,
                out _);
            lieutenant.Loyalty = Defection.BreakingPoint;

            var incidents = new List<Incident>();
            var report = Defection.Tick(roster, lieutenant, 30, incidents);
            if (!report.Happened || report.TookWithHim.Length == 0)
            {
                failures.Add("TheBookSaysHowManyWentWithHim: nobody went over at all.");
                return;
            }

            var count = report.TookWithHim.Length;

            // The paper names the figure.
            var printed = "";
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Defected)
                    printed = incidents[i].Line;
            if (printed.IndexOf(count.ToString(), StringComparison.Ordinal) < 0 &&
                !(count == 1 && printed.IndexOf("one", StringComparison.Ordinal) >= 0))
                failures.Add("TheBookSaysHowManyWentWithHim: the paper said \"" +
                             printed + "\" and never how many.");

            // FOLLOW-002. A report with no door named still prints the words the book
            // printed before anybody worked out where he went.
            if (report.Family.Length != 0 || report.ToGangId >= 0)
                failures.Add("TheBookSaysHowManyWentWithHim: an unhanded door came " +
                             "back naming a house.");
            if (printed.IndexOf("has gone over", StringComparison.Ordinal) < 0)
                failures.Add("TheBookSaysHowManyWentWithHim: the paper stopped saying " +
                             "he went over at all - \"" + printed + "\".");

            // His own file says he went over, and does not say it twice.
            var his = lieutenant.Career[lieutenant.Career.Count - 1];
            if (his.Line.IndexOf("Went over", StringComparison.Ordinal) < 0)
                failures.Add("TheBookSaysHowManyWentWithHim: his file closes on \"" +
                             his.Line + "\".");
            if (his.Weight != Notability.WeightOf(IncidentKind.Defected))
                failures.Add("TheBookSaysHowManyWentWithHim: going over counts for " +
                             $"{his.Weight}, not what a defection is worth.");
            Career.FromIncident(lieutenant, new Incident(lieutenant.Id,
                lieutenant.FullName, IncidentKind.Defected, 30, "", 0, printed));
            if (lieutenant.Career[lieutenant.Career.Count - 1] != his)
                failures.Add("TheBookSaysHowManyWentWithHim: the feed wrote the " +
                             "defection onto his file a second time.");

            // And every man who followed him says so.
            for (var i = 0; i < report.TookWithHim.Length; i++)
            {
                var follower = roster.Find(report.TookWithHim[i]);
                var last = follower.Career[follower.Career.Count - 1];
                if (last.Line.IndexOf(lieutenant.FullName, StringComparison.Ordinal) < 0)
                    failures.Add("TheBookSaysHowManyWentWithHim: " + follower.FullName +
                                 "'s file says \"" + last.Line + "\" - it does not say " +
                                 "who he followed.");
            }

            // A man nobody would follow goes alone, and the page says that too.
            var alone = BareOutfit();
            var loner = AddCrew(alone, "Loner", AttributeScale.MinHalfSteps, 3, 90, out _);
            loner.Loyalty = Defection.BreakingPoint;
            var solo = new List<Incident>();
            var lonely = Defection.Tick(alone, loner, 31, solo);
            if (lonely.TookWithHim.Length != 0)
                failures.Add("TheBookSaysHowManyWentWithHim: the fixture's loner took " +
                             "men after all.");
            if (solo.Count == 0 ||
                solo[0].Line.IndexOf("Nobody would follow", StringComparison.Ordinal) < 0)
                failures.Add("TheBookSaysHowManyWentWithHim: a man who went alone got " +
                             "the same line as one who emptied his branch.");
        }

        // ------------------------------------------ FOLLOW-002, somewhere to go

        /// <summary>
        /// A defector goes SOMEWHERE, the destination is a formula rather than a roll,
        /// and every page that talks about the night names the house.
        ///
        /// The rule is asserted, never a name: the house he goes to is the one whose
        /// claim is loudest, and the claim is the ground it shares with us plus what
        /// the standing with it is worth.
        /// </summary>
        static void ADefectorHasSomewhereToGo(List<string> failures)
        {
            // Two rivals. One shares a street with us and we are at peace with it; the
            // other holds more ground and we are at war with it. A shared block is
            // worth more than the whole stance scale, so the neighbour takes him.
            var neighbour = new OpenDoor(1, 2, Stance.Peace, 3);
            var enemy = new OpenDoor(2, 0, Stance.War, 40);

            var doors = new List<OpenDoor> { enemy, neighbour };
            var picked = OpenDoors.Pick(doors);
            if (picked.GangId != neighbour.GangId)
                failures.Add("ADefectorHasSomewhereToGo: the door opened at gang " +
                             picked.GangId + " and not at the house he has been " +
                             "standing across the street from.");
            if (!picked.IsKnown || picked.Family.Length == 0)
                failures.Add("ADefectorHasSomewhereToGo: the house came back nameless.");

            // With nothing to choose between them the bigger house takes him - nobody
            // is ever homeless, and no branch of this is a die roll.
            var quiet = OpenDoors.Pick(new List<OpenDoor>
            {
                new OpenDoor(3, 0, Stance.Peace, 2),
                new OpenDoor(4, 0, Stance.Peace, 9),
            });
            if (quiet.GangId != 4)
                failures.Add("ADefectorHasSomewhereToGo: with no claim on either side " +
                             "he went to gang " + quiet.GangId + " rather than the " +
                             "house holding the most ground.");

            // And the claim is exactly what the class says it is.
            if (OpenDoors.ClaimOf(neighbour) !=
                2 * OpenDoors.PerShoulder + OpenDoors.AtPeace)
                failures.Add("ADefectorHasSomewhereToGo: the claim is not the sum the " +
                             "rule states.");

            // The whole night, end to end: the paper, his file and the report all name
            // the same house.
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Bruno", AttributeScale.MaxHalfSteps, 4, 90,
                out _);
            lieutenant.Loyalty = Defection.BreakingPoint;

            var incidents = new List<Incident>();
            var report = Defection.Tick(roster, lieutenant, 40, incidents, picked);
            if (report.ToGangId != picked.GangId)
                failures.Add("ADefectorHasSomewhereToGo: the report lost the door.");

            var printed = "";
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Defected)
                    printed = incidents[i].Line;
            if (printed.IndexOf(picked.Family, StringComparison.Ordinal) < 0)
                failures.Add("ADefectorHasSomewhereToGo: the paper said \"" + printed +
                             "\" and never which house took him.");

            var his = lieutenant.Career[lieutenant.Career.Count - 1].Line;
            if (his.IndexOf(picked.Family, StringComparison.Ordinal) < 0)
                failures.Add("ADefectorHasSomewhereToGo: his file closes on \"" + his +
                             "\" and never names the house.");

            // Nothing is left dangling by the men who followed him, exactly as before.
            for (var i = 0; i < report.TookWithHim.Length; i++)
            {
                var follower = roster.Find(report.TookWithHim[i]);
                if (follower == null || follower.Status != CharacterStatus.Deserted)
                    failures.Add("ADefectorHasSomewhereToGo: a man who walked out " +
                                 "behind him is still on the books.");
            }
        }

        /// <summary>
        /// Same city, same door - every time. The reading is taken off holdings and
        /// stances, and running it twice over the same city has to answer the same
        /// house, or a save and a reload would send a man somewhere else.
        /// </summary>
        static void TheSameCityOpensTheSameDoor(List<string> failures)
        {
            var holdings = new List<Turf.Holding>();
            // Us on blocks 5 and 6; gang 7 on block 6 with us; gang 3 far away with
            // more ground than anybody.
            holdings.Add(new Turf.Holding(Gangs.GangCatalog.PlayerGangId, 5));
            holdings.Add(new Turf.Holding(Gangs.GangCatalog.PlayerGangId, 6));
            holdings.Add(new Turf.Holding(7, 6));
            holdings.Add(new Turf.Holding(7, 6));
            for (var i = 0; i < 12; i++)
                holdings.Add(new Turf.Holding(3, 20 + i));

            var relations = new HouseRelations();
            var first = new List<OpenDoor>();
            var second = new List<OpenDoor>();
            OpenDoors.Read(holdings, relations, Gangs.GangCatalog.PlayerGangId, first);
            OpenDoors.Read(holdings, relations, Gangs.GangCatalog.PlayerGangId, second);

            var a = OpenDoors.Pick(first);
            var b = OpenDoors.Pick(second);
            if (a.GangId != b.GangId)
                failures.Add("TheSameCityOpensTheSameDoor: the same city answered " +
                             a.GangId + " and then " + b.GangId + ".");
            if (a.GangId != 7)
                failures.Add("TheSameCityOpensTheSameDoor: he walked to gang " +
                             a.GangId + " and not to the house sharing his street.");

            // Two of that house's buildings on ONE block are one street's worth of
            // acquaintance, not two.
            for (var i = 0; i < first.Count; i++)
                if (first[i].GangId == 7 && first[i].Shoulders != 1)
                    failures.Add("TheSameCityOpensTheSameDoor: two shops on one block " +
                                 "counted as " + first[i].Shoulders + " shoulders.");
        }

        /// <summary>
        /// Twenty-one houses tick twenty-one runners, and each of them has to answer
        /// out of ITS OWN city. A rival that believed it was house zero offered its own
        /// defectors house zero's doors - and, reading a city it had never been handed,
        /// sent every one of them through the lowest id on the table.
        /// </summary>
        static void EveryHouseWalksOutOfItsOwnCity(List<string> failures)
        {
            // One city, read by two different houses. Gang 7 shares a block with gang 1
            // and holds nothing else; gang 1 is the biggest house in town.
            var holdings = new List<Turf.Holding>();
            holdings.Add(new Turf.Holding(7, 6));
            holdings.Add(new Turf.Holding(1, 6));
            for (var i = 0; i < 9; i++)
                holdings.Add(new Turf.Holding(1, 20 + i));
            holdings.Add(new Turf.Holding(Gangs.GangCatalog.PlayerGangId, 40));

            var relations = new HouseRelations();
            var doors = new List<OpenDoor>();

            // House 7 reads it: its own id is never on the table, and the house it
            // stands beside takes the man.
            OpenDoors.Read(holdings, relations, 7, doors);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].GangId == 7)
                    failures.Add("EveryHouseWalksOutOfItsOwnCity: house 7 was offered " +
                                 "its own door.");
            var seven = OpenDoors.Pick(doors);
            if (seven.GangId != 1)
                failures.Add("EveryHouseWalksOutOfItsOwnCity: house 7's man walked to " +
                             seven.GangId + " and not to the house on his own block.");

            // House 1 reads the same city and answers differently, because the shoulder
            // it shares is with SEVEN.
            OpenDoors.Read(holdings, relations, 1, doors);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].GangId == 1)
                    failures.Add("EveryHouseWalksOutOfItsOwnCity: house 1 was offered " +
                                 "its own door.");
            var one = OpenDoors.Pick(doors);
            if (one.GangId != 7)
                failures.Add("EveryHouseWalksOutOfItsOwnCity: house 1's man walked to " +
                             one.GangId + " and not to the house on his own block.");

            // And a house that was never handed a city says so rather than inventing a
            // destination out of the lowest id on the table.
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Blind", AttributeScale.MaxHalfSteps, 3, 90,
                out _);
            lieutenant.Loyalty = Defection.BreakingPoint;

            // The book of standings belongs to the city and is hung on the bench when
            // the underworld is dealt; a fixture that drives one runner hands it one.
            var runner = new CampaignRunner
            {
                GangId = 7,
                Relations = new HouseRelations(),
            };
            runner.Campaign.Day = 5;
            runner.DayTick(roster, payTribute: false);

            if (runner.Defections.Count == 0)
            {
                failures.Add("EveryHouseWalksOutOfItsOwnCity: nobody walked at all.");
                return;
            }
            var filed = runner.Defections[runner.Defections.Count - 1];
            if (filed.GangId >= 0 || filed.Family.Length != 0)
                failures.Add("EveryHouseWalksOutOfItsOwnCity: a house with no city to " +
                             "read named " + filed.Family + " anyway.");
            if (runner.Relations.TryGetPending(runner.GangId, 1, out _))
                failures.Add("EveryHouseWalksOutOfItsOwnCity: a fabricated destination " +
                             "soured a standing.");
        }

        /// <summary>
        /// A stance the player has ALREADY asked for is an order in flight. The two
        /// changes he does not choose - an unpaid levy and a house taking his men -
        /// harden one step and must never take a declared war back down to a truce
        /// because they happened to land in the same midnight.
        /// </summary>
        static void SouringNeverSoftensAnOrderHeGave(List<string> failures)
        {
            var roster = BareOutfit();
            var lieutenant = AddCrew(roster, "Turncoat", AttributeScale.MaxHalfSteps, 4,
                90, out _);
            lieutenant.Loyalty = 0;

            var runner = new CampaignRunner
            {
                GangId = Gangs.GangCatalog.PlayerGangId,
                Relations = new HouseRelations(),
            };
            runner.Campaign.Day = 5;
            var city = new List<Turf.Holding>
            {
                new Turf.Holding(Gangs.GangCatalog.PlayerGangId, 6),
                new Turf.Holding(3, 6),
            };
            runner.HoldingsOf = into => { into.Clear(); into.AddRange(city); };

            // He is at peace with them today and has already declared war for tonight.
            runner.Relations.SetPending(runner.GangId, 3, Stance.War);
            runner.DayTick(roster, payTribute: false);
            // Midnight for the standings belongs to the WHOLE city now - the underworld
            // lands every house's pending stance at once, before anybody's books turn -
            // so a fixture driving one bench has to turn that page itself.
            runner.Relations.ApplyPending();

            if (runner.Defections.Count == 0 ||
                runner.Defections[runner.Defections.Count - 1].GangId != 3)
                failures.Add("SouringNeverSoftensAnOrderHeGave: the fixture's man did " +
                             "not walk to house 3.");
            if (runner.Relations.StanceBetween(runner.GangId, 3) != Stance.War)
                failures.Add("SouringNeverSoftensAnOrderHeGave: his declared war came " +
                             "out of the midnight as " +
                             runner.Relations.StanceBetween(runner.GangId, 3) + ".");

            // An order SOFTENING toward the house that took them does not stand: he
            // asked this morning, they took his men tonight, and the sheet must not
            // shake their hand on the strength of the earlier decision.
            var soft = BareOutfit();
            var third = AddCrew(soft, "Third", AttributeScale.MaxHalfSteps, 4, 90, out _);
            third.Loyalty = 0;
            var softening = new CampaignRunner
            {
                GangId = Gangs.GangCatalog.PlayerGangId,
                Relations = new HouseRelations(),
            };
            softening.Campaign.Day = 5;
            softening.HoldingsOf = into => { into.Clear(); into.AddRange(city); };
            softening.Relations.SetPending(softening.GangId, 3, Stance.Truce);
            softening.Relations.ApplyPending();
            softening.Relations.SetPending(softening.GangId, 3, Stance.Peace);
            softening.DayTick(soft, payTribute: false);
            // The city's midnight, by hand again.
            softening.Relations.ApplyPending();
            if (softening.Relations.StanceBetween(softening.GangId, 3) != Stance.War)
                failures.Add("SouringNeverSoftensAnOrderHeGave: a house that took our " +
                             "men was let down to " +
                             softening.Relations.StanceBetween(softening.GangId, 3) +
                             " by an order he gave before it happened.");

            // And souring still BITES where he has asked for nothing.
            var quiet = BareOutfit();
            var second = AddCrew(quiet, "Second", AttributeScale.MaxHalfSteps, 4, 90,
                out _);
            second.Loyalty = 0;
            var other = new CampaignRunner
            {
                GangId = Gangs.GangCatalog.PlayerGangId,
                Relations = new HouseRelations(),
            };
            other.Campaign.Day = 5;
            other.HoldingsOf = into => { into.Clear(); into.AddRange(city); };
            other.DayTick(quiet, payTribute: false);
            // And again: the souring is written PENDING, so the night has to land.
            other.Relations.ApplyPending();
            if (other.Relations.StanceBetween(other.GangId, 3) != Stance.Truce)
                failures.Add("SouringNeverSoftensAnOrderHeGave: a house that took our " +
                             "men stayed at " +
                             other.Relations.StanceBetween(other.GangId, 3) + ".");
        }

        /// <summary>
        /// The defection BOOK is a rolling window like every other book on the runner;
        /// what a house has taken off us is not. A total that fell as the record
        /// scrolled off the back would be the page saying an irreversible loss had
        /// un-happened.
        /// </summary>
        static void WhatAHouseTookIsNeverForgotten(List<string> failures)
        {
            var runner = new CampaignRunner
            {
                GangId = Gangs.GangCatalog.PlayerGangId,
                Relations = new HouseRelations(),
            };
            var city = new List<Turf.Holding>
            {
                new Turf.Holding(Gangs.GangCatalog.PlayerGangId, 6),
                new Turf.Holding(3, 6),
            };
            runner.HoldingsOf = into => { into.Clear(); into.AddRange(city); };

            var runs = CampaignRunner.RecordsKept + 5;
            var walked = 0;
            var seen = 0;
            for (var n = 0; n < runs; n++)
            {
                var roster = BareOutfit();
                // Flat on the floor rather than exactly on the breaking point: a
                // midnight that happens to land on a drift week pays him a point for
                // being paid the rate, and a man sitting ON the line would be lifted
                // one over it and stay another week.
                var man = AddCrew(roster, "Gone" + n, AttributeScale.MaxHalfSteps, 4, 90,
                    out _);
                man.Loyalty = 0;
                runner.DayTick(roster, payTribute: false);

                var now = runner.MenLostTo(3);
                if (now < seen)
                    failures.Add("WhatAHouseTookIsNeverForgotten: the tally fell from " +
                                 seen + " to " + now + " on defection " + (n + 1) + ".");
                if (now > seen)
                    walked++;
                seen = now;
            }

            if (walked < runs)
                failures.Add("WhatAHouseTookIsNeverForgotten: only " + walked + " of " +
                             runs + " defections reached the tally.");
            if (runner.Defections.Count > CampaignRunner.RecordsKept)
                failures.Add("WhatAHouseTookIsNeverForgotten: the book stopped being a " +
                             "rolling window.");
            if (seen <= 0)
                failures.Add("WhatAHouseTookIsNeverForgotten: the house took nobody.");
        }

        // ------------------------------------------- FOLLOW-001, the reason feed

        /// <summary>
        /// A day is one line per man, not one per point that moved: a midnight that
        /// took two off him for being parked and gave one back for being paid is one
        /// thing that happened to him, and both reasons ride on the one line.
        /// </summary>
        static void TheDayIsFoldedOneLineAMan(List<string> failures)
        {
            var changes = new List<PersonalityChange>
            {
                new PersonalityChange(1, "Rossi", PersonalityTrait.Loyalty, 70, 68,
                    "has been exactly what he is for too long"),
                new PersonalityChange(1, "Rossi", PersonalityTrait.Loyalty, 68, 69,
                    "paid the rate, week after week"),
                new PersonalityChange(2, "Bruno", PersonalityTrait.Loyalty, 50, 38,
                    "took money from somebody else"),
                // A day that gave and took the same point said nothing about him.
                new PersonalityChange(3, "Quiet", PersonalityTrait.Loyalty, 50, 49,
                    "one of more men than his lieutenant can lead"),
                new PersonalityChange(3, "Quiet", PersonalityTrait.Loyalty, 49, 50,
                    "paid the rate, week after week"),
            };

            var feed = new List<ReasonLine>();
            ReasonFeed.Fold(changes, 12, feed);

            if (feed.Count != 2)
            {
                failures.Add("TheDayIsFoldedOneLineAMan: the day folded to " +
                             feed.Count + " lines, not two.");
                return;
            }

            // Biggest swing first.
            if (feed[0].CharacterId != 2 || feed[0].Delta != -12)
                failures.Add("TheDayIsFoldedOneLineAMan: the loudest movement is not " +
                             "at the top.");
            if (feed[1].CharacterId != 1 || feed[1].Delta != -1)
                failures.Add("TheDayIsFoldedOneLineAMan: Rossi's day netted " +
                             feed[1].Delta + " and not the -1 the two movements come to.");

            // Both reasons ride, verbatim.
            if (feed[1].Reason.IndexOf("for too long", StringComparison.Ordinal) < 0 ||
                feed[1].Reason.IndexOf("paid the rate", StringComparison.Ordinal) < 0)
                failures.Add("TheDayIsFoldedOneLineAMan: a reason was dropped - \"" +
                             feed[1].Reason + "\".");

            for (var i = 0; i < feed.Count; i++)
            {
                if (feed[i].Delta == 0)
                    failures.Add("TheDayIsFoldedOneLineAMan: an entry moved nothing.");
                if (feed[i].Reason.Length == 0)
                    failures.Add("TheDayIsFoldedOneLineAMan: an entry carries no reason.");
                if (feed[i].Day != 12)
                    failures.Add("TheDayIsFoldedOneLineAMan: an entry lost its day.");
            }

            // Same history, same lines, same order.
            var again = new List<ReasonLine>();
            ReasonFeed.Fold(changes, 12, again);
            for (var i = 0; i < feed.Count; i++)
                if (again[i].CharacterId != feed[i].CharacterId ||
                    again[i].Delta != feed[i].Delta ||
                    again[i].Reason != feed[i].Reason)
                    failures.Add("TheDayIsFoldedOneLineAMan: the same day folded two " +
                                 "different ways.");
        }

        /// <summary>
        /// A scripted fortnight over a seeded roster, driven through the campaign the
        /// way the game drives it: every movement the sim made reaches the book, every
        /// line has a reason and a real delta, and the list the model writes finally
        /// has a reader.
        /// </summary>
        static void EveryReasonReachesTheBook(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(1987);
            // Ambitious and underpaid, so the parked rule and the greed ladder both
            // have something to say inside a fortnight.
            for (var i = 0; i < roster.Members.Count; i++)
            {
                Personality.Set(roster.Members[i], PersonalityTrait.Ambition, 90);
                Personality.Set(roster.Members[i], PersonalityTrait.Greed, 90);
                roster.Members[i].RankSince = 1;
            }

            var runner = new CampaignRunner();
            runner.Campaign.Day = Loyalty.ParkedDays + 2;
            for (var day = 0; day < 14; day++)
                runner.DayTick(roster, payTribute: false);

            if (runner.ReasonBook.Count == 0)
            {
                failures.Add("EveryReasonReachesTheBook: a fortnight of drift reached " +
                             "the book as nothing at all.");
                return;
            }

            for (var i = 0; i < runner.ReasonBook.Count; i++)
            {
                var line = runner.ReasonBook[i];
                if (line.Delta == 0)
                    failures.Add("EveryReasonReachesTheBook: " + line.Name +
                                 " is on the page for a day that moved nothing.");
                if (line.Reason.Length == 0)
                    failures.Add("EveryReasonReachesTheBook: " + line.Name +
                                 " moved for no printed reason.");
                if (line.Name.Length == 0)
                    failures.Add("EveryReasonReachesTheBook: a line names nobody.");
            }

            // Inside one day the loudest swing is first and no man appears twice for
            // the same trait.
            for (var i = 1; i < runner.ReasonBook.Count; i++)
            {
                var before = runner.ReasonBook[i - 1];
                var line = runner.ReasonBook[i];
                if (before.Day != line.Day)
                    continue;
                if (before.Size < line.Size)
                    failures.Add("EveryReasonReachesTheBook: day " + line.Day +
                                 " is not ordered loudest first.");
                if (before.CharacterId == line.CharacterId &&
                    before.Trait == line.Trait)
                    failures.Add("EveryReasonReachesTheBook: " + line.Name +
                                 " got two lines for one trait on day " + line.Day + ".");
            }
        }

        /// <summary>
        /// The page asks for last night FIRST and the loudest movement of it FIRST -
        /// two orders that pull against each other. A reader walking the flat book
        /// backwards gets only the first of them: it reads the newest day back to
        /// front, so a limited run keeps the day's +1s and drops exactly the swings
        /// the feed exists to show.
        /// </summary>
        static void TheFeedReadsLastNightLoudestFirst(List<string> failures)
        {
            var book = new List<ReasonLine>();

            // An old day, then a busy one - more movements in it than any panel shows.
            var older = new List<PersonalityChange>
            {
                new PersonalityChange(99, "Yesterday", PersonalityTrait.Loyalty, 50, 30,
                    "took money from somebody else"),
            };
            ReasonFeed.Fold(older, 10, book);

            var busy = new List<PersonalityChange>();
            const int men = 20;
            for (var i = 0; i < men; i++)
                busy.Add(new PersonalityChange(i, "Man" + i, PersonalityTrait.Loyalty,
                    60, 60 - (i + 1), "reason " + i));
            ReasonFeed.Fold(busy, 11, book);

            const int shown = 6;
            var page = new List<ReasonLine>();
            ReasonFeed.Latest(book, shown, page);

            if (page.Count != shown)
            {
                failures.Add("TheFeedReadsLastNightLoudestFirst: the page took " +
                             page.Count + " lines and not " + shown + ".");
                return;
            }

            for (var i = 0; i < page.Count; i++)
                if (page[i].Day != 11)
                    failures.Add("TheFeedReadsLastNightLoudestFirst: an older day is on " +
                                 "the page while last night still has lines.");

            // The loudest of that day leads, and the run comes down in order.
            if (page[0].Size != men)
                failures.Add("TheFeedReadsLastNightLoudestFirst: the page opens on a " +
                             "movement of " + page[0].Size + " and not on the day's " +
                             "loudest, " + men + ".");
            for (var i = 1; i < page.Count; i++)
                if (page[i - 1].Size < page[i].Size)
                    failures.Add("TheFeedReadsLastNightLoudestFirst: the run climbs at " +
                                 "line " + (i + 1) + ".");

            // A page with room for everything reads the newest day first and the older
            // one under it, each still loudest-first inside itself.
            var whole = new List<ReasonLine>();
            ReasonFeed.Latest(book, book.Count, whole);
            if (whole.Count != book.Count)
                failures.Add("TheFeedReadsLastNightLoudestFirst: a page with room for " +
                             "the whole book lost lines.");
            if (whole[whole.Count - 1].Day != 10)
                failures.Add("TheFeedReadsLastNightLoudestFirst: the oldest day is not " +
                             "at the bottom.");
        }

        // ---------------------------------------------- FOLLOW-004, time in rank

        /// <summary>
        /// The figure a page prints is the figure the parked rule is charged against,
        /// and a man who has never been anything else is measured from the day he came
        /// on rather than from day zero.
        /// </summary>
        static void TimeInRankIsWhatTheModelReads(List<string> failures)
        {
            var made = new Character { Id = 1, FirstName = "Made", Surname = "Man" };
            made.RankSince = 40;
            if (Loyalty.TimeInRank(made, 100) != 100 - made.RankSince)
                failures.Add("TimeInRankIsWhatTheModelReads: a made man's stretch is " +
                             "not the day minus his stamp.");

            var hood = new Character { Id = 2, FirstName = "Corner", Surname = "Boy" };
            Career.Joined(hood, 30, "");
            if (hood.RankSince != 0)
                failures.Add("TimeInRankIsWhatTheModelReads: the fixture stamped a " +
                             "rank change that never happened.");
            if (Loyalty.RankSinceDay(hood) != 30)
                failures.Add("TimeInRankIsWhatTheModelReads: a man who has never been " +
                             "anything else reads from day " +
                             Loyalty.RankSinceDay(hood) + " and not from the day he " +
                             "came on.");
            if (Loyalty.TimeInRank(hood, 100) != 70)
                failures.Add("TimeInRankIsWhatTheModelReads: his stretch came to " +
                             Loyalty.TimeInRank(hood, 100) + " and not seventy days.");

            // And the reading agrees with what Drift actually charges: an ambitious man
            // exactly ON the line pays nothing, and one day past it pays. Underpaid on
            // both sides, so the "paid the rate" gain cannot mask either answer.
            var settlingDay = Loyalty.DriftEveryDays * 10;

            var shy = new Character { Id = 3, FirstName = "Nearly", Surname = "There" };
            Personality.Set(shy, PersonalityTrait.Ambition, Loyalty.AmbitionFloor + 10);
            shy.Loyalty = 70;
            shy.RankSince = settlingDay - Loyalty.ParkedDays;
            if (Loyalty.TimeInRank(shy, settlingDay) != Loyalty.ParkedDays)
                failures.Add("TimeInRankIsWhatTheModelReads: the fixture is not " +
                             "standing on the parked line.");
            if (Loyalty.IsParked(shy, settlingDay))
                failures.Add("TimeInRankIsWhatTheModelReads: a man exactly at the " +
                             "parked line reads as parked.");
            Loyalty.Drift(shy, true, false, 5, settlingDay,
                Loyalty.TimeInRank(shy, settlingDay), null, null);
            if (shy.Loyalty != 70)
                failures.Add("TimeInRankIsWhatTheModelReads: a man exactly at the " +
                             "parked line was charged for it.");

            var past = new Character { Id = 4, FirstName = "Long", Surname = "Parked" };
            Personality.Set(past, PersonalityTrait.Ambition, Loyalty.AmbitionFloor + 10);
            past.Loyalty = 70;
            past.RankSince = settlingDay - Loyalty.ParkedDays - 1;
            if (!Loyalty.IsParked(past, settlingDay))
                failures.Add("TimeInRankIsWhatTheModelReads: a man one day past the " +
                             "line does not read as parked.");
            Loyalty.Drift(past, true, false, 5, settlingDay,
                Loyalty.TimeInRank(past, settlingDay), null, null);
            if (past.Loyalty != 70 - Loyalty.ParkedLoss)
                failures.Add("TimeInRankIsWhatTheModelReads: the parked man went to " +
                             past.Loyalty + " and not the scheduled " +
                             (70 - Loyalty.ParkedLoss) + ".");
        }

        static void ALoyalOutfitNeverLosesAMan(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(1987);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                roster.Members[i].Loyalty = 90;
                Personality.Set(roster.Members[i], PersonalityTrait.Ambition, 30);
            }

            var incidents = new List<Incident>();
            var changes = new List<PersonalityChange>();
            var branch = new OrganizationQuery(roster);

            for (var day = 1; day <= 365; day++)
            {
                roster.Day = day;
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    var hasSuperior = branch.TryGetCommandParent(member.Id, out var above);
                    Loyalty.Drift(member, hasSuperior,
                        hasSuperior && branch.CapacityOf(above.Id).IsOverCapacity,
                        Wages.PayGap(member), day, day - member.RankSince, changes,
                        incidents);
                }

                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Rank == Rank.Lieutenant && !member.Gone)
                        Defection.Tick(roster, member, day, incidents);
                }
            }

            for (var i = 0; i < roster.Members.Count; i++)
                if (roster.Members[i].Status == CharacterStatus.Deserted)
                    failures.Add("ALoyalOutfitNeverLosesAMan: " +
                                 roster.Members[i].FullName + " walked out of a loyal " +
                                 "outfit over one year.");
        }

        // ------------------------------------------------- LOY-003, the promotion

        static void PromotionChangesRankAndNothingElse(List<string> failures)
        {
            var roster = BareOutfit();
            var hood = AddHood(roster, "Rising");
            for (var a = 0; a < AttributeScale.Count; a++)
                hood.SetHalfSteps((CharacterAttribute)a, 4 + a % 5);
            hood.Look = "man_rising";

            var before = new int[AttributeScale.Count];
            for (var a = 0; a < AttributeScale.Count; a++)
                before[a] = hood.GetHalfSteps((CharacterAttribute)a);
            var wageBefore = Wages.WageFor(hood);

            roster.Day = 40;
            var incidents = new List<Incident>();
            var result = RosterOps.Promote(roster, hood.Id, out var crewId, incidents);
            if (!result.Ok)
            {
                failures.Add("PromotionChangesRankAndNothingElse: refused - " +
                             result.Reason);
                return;
            }

            for (var a = 0; a < AttributeScale.Count; a++)
                if (hood.GetHalfSteps((CharacterAttribute)a) != before[a])
                    failures.Add("PromotionChangesRankAndNothingElse: " +
                                 (CharacterAttribute)a + " moved on promotion.");

            if (hood.Rank != Rank.Lieutenant)
                failures.Add("PromotionChangesRankAndNothingElse: he is not a lieutenant.");
            if (hood.Look != "man_rising")
                failures.Add("PromotionChangesRankAndNothingElse: he walked out a " +
                             "different man.");
            if (hood.RankSince != 40)
                failures.Add("PromotionChangesRankAndNothingElse: the rank clock reads " +
                             hood.RankSince + ", not the day it happened.");
            if (roster.FindCrew(crewId) == null)
                failures.Add("PromotionChangesRankAndNothingElse: no crew formed.");
            // WAGE-001. A promotion is a RISE, never merely a change: the lieutenant
            // base sits above the hood ceiling by construction, so making a man can
            // never cut his pay.
            if (Wages.WageFor(hood) <= wageBefore)
                failures.Add($"PromotionChangesRankAndNothingElse: he drew " +
                             $"{wageBefore} as a hood and {Wages.WageFor(hood)} as a " +
                             "lieutenant - a promotion must pay more.");

            var announced = false;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Promoted &&
                    incidents[i].CharacterId == hood.Id)
                    announced = true;
            if (!announced)
                failures.Add("PromotionChangesRankAndNothingElse: the paper carried " +
                             "nothing.");
        }

        static void PromotionIsRefusedPastTheBossesSpan(List<string> failures)
        {
            var roster = BareOutfit();
            var boss = roster.FindBoss();
            // A Boss the street has barely heard of holds very few branches.
            boss.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MinHalfSteps);
            boss.SetHalfSteps(CharacterAttribute.StreetAuthority,
                AttributeScale.MinHalfSteps);
            var span = Command.LieutenantCap(boss);

            var refusedAt = -1;
            for (var i = 0; i < span + 2; i++)
            {
                var hood = AddHood(roster, "N" + i);
                if (!RosterOps.Promote(roster, hood.Id, out _).Ok && refusedAt < 0)
                    refusedAt = i;
            }

            if (refusedAt != span)
                failures.Add($"PromotionIsRefusedPastTheBossesSpan: refused at " +
                             $"{refusedAt} with a span of {span}.");
        }

        static void ASpecialistIsNeverPromoted(List<string> failures)
        {
            var roster = BareOutfit();
            var clerk = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Sol", Surname = "Katz",
                Specialty = Specialty.Accountant,
            };
            roster.Members.Add(clerk);

            if (RosterOps.Promote(roster, clerk.Id, out _).Ok)
                failures.Add("ASpecialistIsNeverPromoted: the accountant got a crew.");

            // A specialist may roll fine numbers; the book must not offer him, because
            // the offer is one the ledger cannot honour.
            for (var a = 0; a < AttributeScale.Count; a++)
                clerk.SetHalfSteps((CharacterAttribute)a, AttributeScale.MaxHalfSteps);
            Personality.Set(clerk, PersonalityTrait.Courage, 100);
            Personality.Set(clerk, PersonalityTrait.Discipline, 100);
            var marks = ManFlags.Of(clerk);
            if ((marks & ManFlag.LieutenantMaterial) != 0)
                failures.Add("ASpecialistIsNeverPromoted: the book marked the " +
                             "accountant as lieutenant material.");
            if ((marks & ManFlag.HitmanMaterial) != 0)
                failures.Add("ASpecialistIsNeverPromoted: the book offered the " +
                             "accountant as a gun.");
        }

        static void TheOldCrewFeelsHimRise(List<string> failures)
        {
            var roster = BareOutfit();
            AddCrew(roster, "Bruno", 8, 0, 50, out var crew);

            var hungry = AddHood(roster, "Hungry");
            Personality.Set(hungry, PersonalityTrait.Ambition, 90);
            RosterOps.AssignToCrew(roster, hungry.Id, crew.Id);
            hungry.Loyalty = 60;

            var settled = AddHood(roster, "Settled");
            Personality.Set(settled, PersonalityTrait.Ambition, 10);
            RosterOps.AssignToCrew(roster, settled.Id, crew.Id);
            settled.Loyalty = 60;

            var rising = AddHood(roster, "Rising");
            RosterOps.AssignToCrew(roster, rising.Id, crew.Id);

            var changes = new List<PersonalityChange>();
            if (!RosterOps.Promote(roster, rising.Id, out _, null, changes).Ok)
            {
                failures.Add("TheOldCrewFeelsHimRise: the promotion was refused.");
                return;
            }

            if (hungry.Loyalty >= 60)
                failures.Add("TheOldCrewFeelsHimRise: the ambitious man was not moved " +
                             $"by being passed over - {hungry.Loyalty}.");
            if (settled.Loyalty <= 60)
                failures.Add("TheOldCrewFeelsHimRise: the settled man took nothing from " +
                             $"one of his own rising - {settled.Loyalty}.");

            var said = 0;
            for (var i = 0; i < changes.Count; i++)
                if (changes[i].CharacterId == hungry.Id ||
                    changes[i].CharacterId == settled.Id)
                {
                    said++;
                    if (changes[i].Reason.Length == 0)
                        failures.Add("TheOldCrewFeelsHimRise: a ripple moved a man for " +
                                     "no stated reason.");
                }
            if (said < 2)
                failures.Add($"TheOldCrewFeelsHimRise: {said} of the old crew were " +
                             "recorded, not both.");
        }

        static void ADemotionIsAllowedAndBrutal(List<string> failures)
        {
            var roster = BareOutfit();
            var settled = AddCrew(roster, "Quiet", 8, 0, 50, out _);
            Personality.Set(settled, PersonalityTrait.Ambition, 10);
            settled.Loyalty = 80;

            var hungry = AddCrew(roster, "Hungry", 8, 0, 50, out _);
            Personality.Set(hungry, PersonalityTrait.Ambition, 100);
            hungry.Loyalty = 80;

            var incidents = new List<Incident>();
            if (!RosterOps.Demote(roster, settled.Id, incidents).Ok ||
                !RosterOps.Demote(roster, hungry.Id, incidents).Ok)
            {
                failures.Add("ADemotionIsAllowedAndBrutal: a demotion was refused.");
                return;
            }

            if (settled.Rank != Rank.Hood || hungry.Rank != Rank.Hood)
                failures.Add("ADemotionIsAllowedAndBrutal: he kept his rank.");
            if (settled.Loyalty >= Loyalty.Neutral)
                failures.Add("ADemotionIsAllowedAndBrutal: it cost him nothing - " +
                             $"{settled.Loyalty}.");
            if (hungry.Loyalty >= settled.Loyalty)
                failures.Add("ADemotionIsAllowedAndBrutal: the ambitious man took it as " +
                             $"well as the settled one - {hungry.Loyalty} against " +
                             $"{settled.Loyalty}.");

            var printed = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Demoted)
                    printed++;
            if (printed != 2)
                failures.Add($"ADemotionIsAllowedAndBrutal: {printed} demotions reached " +
                             "the paper, not two.");
        }

        // ----------------------------------------------------- LOY-004, the marks

        static Character Marked(int combat, int leadership, int organization,
            int authority, int courage, int discipline, int ambition, int loyalty)
        {
            var man = new Character { Id = 1, FirstName = "Man", Surname = "Marked" };
            man.SetHalfSteps(CharacterAttribute.Combat,
                AttributeScale.HalfStepsFor(combat));
            man.SetHalfSteps(CharacterAttribute.Leadership,
                AttributeScale.HalfStepsFor(leadership));
            man.SetHalfSteps(CharacterAttribute.Organization,
                AttributeScale.HalfStepsFor(organization));
            man.SetHalfSteps(CharacterAttribute.StreetAuthority,
                AttributeScale.HalfStepsFor(authority));
            Personality.Set(man, PersonalityTrait.Courage, courage);
            Personality.Set(man, PersonalityTrait.Discipline, discipline);
            Personality.Set(man, PersonalityTrait.Ambition, ambition);
            man.Loyalty = loyalty;
            return man;
        }

        static void TheThreeFlagsAnswerAtTheirThresholds(List<string> failures)
        {
            var officer = Marked(0, ManFlags.LeadershipForCrew,
                ManFlags.OrganizationForCrew, ManFlags.StreetAuthorityForCrew,
                0, 0, 0, 100);
            if ((ManFlags.Of(officer) & ManFlag.LieutenantMaterial) == 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a man exactly on " +
                             "the three command thresholds is not lieutenant material.");

            var short1 = Marked(0, ManFlags.LeadershipForCrew - 10,
                ManFlags.OrganizationForCrew, ManFlags.StreetAuthorityForCrew,
                0, 0, 0, 100);
            if ((ManFlags.Of(short1) & ManFlag.LieutenantMaterial) != 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a man short on " +
                             "Leadership is still lieutenant material.");

            var gun = Marked(ManFlags.CombatForGun, 0, 0, 0, ManFlags.CourageForGun,
                ManFlags.DisciplineForGun, 0, 100);
            if ((ManFlags.Of(gun) & ManFlag.HitmanMaterial) == 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a man exactly on " +
                             "the three gun thresholds is not hitman material.");

            var coward = Marked(ManFlags.CombatForGun, 0, 0, 0,
                ManFlags.CourageForGun - 1, ManFlags.DisciplineForGun, 0, 100);
            if ((ManFlags.Of(coward) & ManFlag.HitmanMaterial) != 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a man one point " +
                             "short of the nerve is still hitman material.");

            var red = Marked(0, 0, 0, 0, 0, 0, ManFlags.AmbitionForRedFlag,
                ManFlags.LoyaltyForRedFlag);
            if ((ManFlags.Of(red) & ManFlag.RedFlag) == 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a hungry man who " +
                             "is not ours carries no red flag.");

            var safe = Marked(0, 0, 0, 0, 0, 0, ManFlags.AmbitionForRedFlag,
                ManFlags.LoyaltyForRedFlag + 1);
            if ((ManFlags.Of(safe) & ManFlag.RedFlag) != 0)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: a man one point " +
                             "over the loyalty line carries a red flag.");

            var dead = Marked(100, 100, 100, 100, 100, 100, 100, 0);
            dead.Status = CharacterStatus.Dead;
            if (ManFlags.Of(dead) != ManFlag.None)
                failures.Add("TheThreeFlagsAnswerAtTheirThresholds: the book still has " +
                             "an opinion about a dead man.");
        }

        static void TheRoundingIsStatedAtTheBoundary(List<string> failures)
        {
            // The half-star scale only lands on multiples of ten, so 55 is met at 60
            // and 54 at 50. The helper is the ONE place that decides it.
            var pairs = new[] { (50, 5), (54, 5), (55, 6), (60, 6), (70, 7) };
            for (var i = 0; i < pairs.Length; i++)
            {
                var (value, halfSteps) = pairs[i];
                if (AttributeScale.HalfStepsFor(value) != halfSteps)
                    failures.Add($"TheRoundingIsStatedAtTheBoundary: {value} lands on " +
                                 $"{AttributeScale.HalfStepsFor(value)} half-steps, not " +
                                 $"{halfSteps}.");
            }

            // And the flag reads it the same way: six half-steps meets "at least 55".
            var man = new Character { Id = 1, FirstName = "M", Surname = "Boundary" };
            man.SetHalfSteps(CharacterAttribute.Leadership,
                AttributeScale.HalfStepsFor(ManFlags.LeadershipForCrew));
            man.SetHalfSteps(CharacterAttribute.Organization, 6);
            man.SetHalfSteps(CharacterAttribute.StreetAuthority,
                AttributeScale.HalfStepsFor(ManFlags.StreetAuthorityForCrew));
            if ((ManFlags.Of(man) & ManFlag.LieutenantMaterial) == 0)
                failures.Add("TheRoundingIsStatedAtTheBoundary: six half-steps of " +
                             "Organization does not meet a threshold of 55.");

            man.SetHalfSteps(CharacterAttribute.Organization, 5);
            if ((ManFlags.Of(man) & ManFlag.LieutenantMaterial) != 0)
                failures.Add("TheRoundingIsStatedAtTheBoundary: five half-steps of " +
                             "Organization meets a threshold of 55.");
        }

        static void CrossingIntoAFlagIsNewsExactlyOnce(List<string> failures)
        {
            var man = Marked(0, 0, 0, 0, 0, 0, 0, 100);
            var incidents = new List<Incident>();

            ManFlags.Announce(man, 1, incidents);
            if (incidents.Count != 0)
                failures.Add("CrossingIntoAFlagIsNewsExactlyOnce: an unremarkable man " +
                             "made the paper.");

            man.SetHalfSteps(CharacterAttribute.Leadership,
                AttributeScale.HalfStepsFor(ManFlags.LeadershipForCrew));
            man.SetHalfSteps(CharacterAttribute.Organization,
                AttributeScale.HalfStepsFor(ManFlags.OrganizationForCrew));
            man.SetHalfSteps(CharacterAttribute.StreetAuthority,
                AttributeScale.HalfStepsFor(ManFlags.StreetAuthorityForCrew));

            ManFlags.Announce(man, 2, incidents);
            for (var day = 3; day < 60; day++)
                ManFlags.Announce(man, day, incidents);

            var said = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.ReadyForACrew)
                    said++;
            if (said != 1)
                failures.Add($"CrossingIntoAFlagIsNewsExactlyOnce: said {said} times, " +
                             "not once.");

            // Falling back out is silent, and climbing back in is news again.
            man.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MinHalfSteps);
            ManFlags.Announce(man, 61, incidents);
            man.SetHalfSteps(CharacterAttribute.Organization,
                AttributeScale.HalfStepsFor(ManFlags.OrganizationForCrew));
            ManFlags.Announce(man, 62, incidents);

            said = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.ReadyForACrew)
                    said++;
            if (said != 2)
                failures.Add($"CrossingIntoAFlagIsNewsExactlyOnce: a second crossing " +
                             $"produced {said} lines in all, not two.");

            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Line.Length == 0)
                    failures.Add("CrossingIntoAFlagIsNewsExactlyOnce: a flag line went " +
                                 "to the paper empty.");
        }

        static void AFlagNeverActsByItself(List<string> failures)
        {
            // Two identical men at the breaking point, one of them carrying the red
            // flag and one not. The arithmetic reads the numbers, not the mark, so the
            // outcome is the same for both.
            var roster = BareOutfit();
            var flagged = AddCrew(roster, "Flagged", 8, 2, 90, out _);
            Personality.Set(flagged, PersonalityTrait.Ambition,
                ManFlags.AmbitionForRedFlag);
            flagged.Loyalty = Defection.BreakingPoint;
            flagged.FlagsAnnounced = ManFlag.RedFlag;

            var quiet = AddCrew(roster, "Quiet", 8, 2, 90, out _);
            Personality.Set(quiet, PersonalityTrait.Ambition, 0);
            quiet.Loyalty = Defection.BreakingPoint;

            var one = Defection.Tick(roster, flagged, 20, null);
            var two = Defection.Tick(roster, quiet, 20, null);

            if (!one.Happened || !two.Happened)
                failures.Add("AFlagNeverActsByItself: the mark decided whether a man " +
                             "walked.");
            if (one.TookWithHim.Length != two.TookWithHim.Length)
                failures.Add("AFlagNeverActsByItself: the flagged man took " +
                             $"{one.TookWithHim.Length} out and the unflagged one " +
                             $"{two.TookWithHim.Length}.");
        }
    }
}
