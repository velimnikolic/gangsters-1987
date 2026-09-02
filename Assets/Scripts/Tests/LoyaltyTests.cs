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
