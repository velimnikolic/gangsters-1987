using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Save;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// The law and order track's contracts (EPICs 17-21): the fight-or-surrender roll,
    /// a precinct's roster and its watch, the sentence a deed earns, the pipe from the
    /// station to a release date, and what being a wanted man costs and cures.
    ///
    /// Pure C#, no UnityEngine, failures returned as data - the whole police MODEL is
    /// engine-free on purpose, so this suite runs in a bare .NET host beside the roster
    /// suites it sits next to.
    /// </summary>
    public static class PoliceTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("NerveAndTemperDecideTheAnswer", NerveAndTemperDecideTheAnswer),
            ("TheAnswerIsTheSameTwiceForOneSeed", TheAnswerIsTheSameTwiceForOneSeed),
            ("ThreeAnswersUseOneDeterministicStream", ThreeAnswersUseOneDeterministicStream),
            ("TheLeaningIsReadableWithoutANumber", TheLeaningIsReadableWithoutANumber),
            ("ARosterNeverGoesAboveItsStrength", ARosterNeverGoesAboveItsStrength),
            ("AHoleIsFilledOnItsOwnDayAndNoSooner", AHoleIsFilledOnItsOwnDayAndNoSooner),
            ("AnEmptyPrecinctSaysSoOnThePlaque", AnEmptyPrecinctSaysSoOnThePlaque),
            ("TheWatchTurnsOnTheHour", TheWatchTurnsOnTheHour),
            ("TheNightHasTheCarsAndTheDayTheFeet", TheNightHasTheCarsAndTheDayTheFeet),
            ("NobodyIsOnDutyWhoIsNotOnTheRoster", NobodyIsOnDutyWhoIsNotOnTheRoster),
            ("TheDeedDecidesTheSentence", TheDeedDecidesTheSentence),
            ("AssaultIsWorseThanAffrayAndBetterThanMurder", AssaultIsWorseThanAffrayAndBetterThanMurder),
            ("ASecondActNeverDowngradesTheCharge", ASecondActNeverDowngradesTheCharge),
            ("LifeIsASentinelAndNotAnOverflow", LifeIsASentinelAndNotAnOverflow),
            ("AnEscapeCostsHimTheSurcharge", AnEscapeCostsHimTheSurcharge),
            ("RunningCostsTwoMoreDays", RunningCostsTwoMoreDays),
            ("ExtraChargesAddDays", ExtraChargesAddDays),
            ("SprungRecordsAnEscapeWithoutABooking", SprungRecordsAnEscapeWithoutABooking),
            ("TheAnswerSurvivesASave", TheAnswerSurvivesASave),
            ("RebookingKeepsTheWorstAnswer", RebookingKeepsTheWorstAnswer),
            ("InCustodyRefusesEveryOrder", InCustodyRefusesEveryOrder),
            ("CarsForPrisoners", CarsForPrisoners),
            ("GAN315_ArrestedManRaisesHisHands", GAN315_ArrestedManRaisesHisHands),
            ("GAN315_PoliceKeepEveryPrisonerCovered", GAN315_PoliceKeepEveryPrisonerCovered),
            ("GAN315_PrisonerBoardsOnlyWithHisEscort", GAN315_PrisonerBoardsOnlyWithHisEscort),
            ("GAN315_NormalBoardingNeverSpringsCustody", GAN315_NormalBoardingNeverSpringsCustody),
            ("GAN315_RightClickCannotReleaseAPrisoner", GAN315_RightClickCannotReleaseAPrisoner),
            ("GAN315_OfficerReturnsToTheCarPromptly", GAN315_OfficerReturnsToTheCarPromptly),
            ("GAN315_ShopStatementRequiresARealEntry", GAN315_ShopStatementRequiresARealEntry),
            ("GAN315_OneUniformAndAFastDispatch", GAN315_OneUniformAndAFastDispatch),
            ("GAN315_DispatchUsesAirDistanceNotTravelTime", GAN315_DispatchUsesAirDistanceNotTravelTime),
            ("GAN315_ComplaintWaitsForPhysicalArrival", GAN315_ComplaintWaitsForPhysicalArrival),
            ("TheNearestPairComesAndACarPastOneFifty", TheNearestPairComesAndACarPastOneFifty),
            ("EveryNearbyPatrolJoinsAndDispatchSendsAtMostOneCar", EveryNearbyPatrolJoinsAndDispatchSendsAtMostOneCar),
            ("PoliceFireCanBeAnsweredWithoutSummoningTheSwarm", PoliceFireCanBeAnsweredWithoutSummoningTheSwarm),
            ("AStalledPairIsAtTheSceneOrSentBack", AStalledPairIsAtTheSceneOrSentBack),
            ("GAN315_BoardingDoesNotResetALiveRoute", GAN315_BoardingDoesNotResetALiveRoute),
            ("GAN315_EscortStandsClearAndTheCarParks", GAN315_EscortStandsClearAndTheCarParks),
            ("GAN315_TransferTracksOnlyPhysicalCustody", GAN315_TransferTracksOnlyPhysicalCustody),
            ("GAN315_ResponseRunsAndFlightDrawsFire", GAN315_ResponseRunsAndFlightDrawsFire),
            ("ROAD000_BlastAccountingDoesNotDoubleCount", ROAD000_BlastAccountingDoesNotDoubleCount),
            ("ROAD001_TheCarriageNeedsPhysicalEdges", ROAD001_TheCarriageNeedsPhysicalEdges),
            ("ROAD002_AKilledPrisonerClosesHisCase", ROAD002_AKilledPrisonerClosesHisCase),
            ("ROAD003_JeopardyIsCapped", ROAD003_JeopardyIsCapped),
            ("ROAD003_PreSeatAmbushIsASpring", ROAD003_PreSeatAmbushIsASpring),
            ("ROAD004_TheWalkHomeIsBounded", ROAD004_TheWalkHomeIsBounded),
            ("ROAD004_DeadTinLeavesTheFleetBook", ROAD004_DeadTinLeavesTheFleetBook),
            ("ROAD004_ExceptionalStagesHaveBackstops", ROAD004_ExceptionalStagesHaveBackstops),
            ("ROAD006_ALiveJourneySavesAtItsSource", ROAD006_ALiveJourneySavesAtItsSource),
            ("HeldMeansHeldUntilAJudgeSaysOtherwise", HeldMeansHeldUntilAJudgeSaysOtherwise),
            ("TheVerdictLandsWhenTheTransferArrives", TheVerdictLandsWhenTheTransferArrives),
            ("AWreckedTransferIsAFreeManUnarmed", AWreckedTransferIsAFreeManUnarmed),
            ("NoCarNoConvoyAndHeWaitsADay", NoCarNoConvoyAndHeWaitsADay),
            ("HiddenDaysClearTheGradeAndSightingsResetThem", HiddenDaysClearTheGradeAndSightingsResetThem),
            ("ShotAtOfficerCoolsInSevenDays", ShotAtOfficerCoolsInSevenDays),
            ("SeverityOrdersTheMarks", SeverityOrdersTheMarks),
            ("AMissIsNotACopKiller", AMissIsNotACopKiller),
            ("ACopKillerNeverComesClean", ACopKillerNeverComesClean),
            ("AMarkIsNeverDowngraded", AMarkIsNeverDowngraded),
            ("OutOfTownDrawsNoWage", OutOfTownDrawsNoWage),
            ("ADeputyRunsTheCrewWhileTheLeaderIsInside", ADeputyRunsTheCrewWhileTheLeaderIsInside),
            ("TheLeaderKeepsHisBranchOnPaper", TheLeaderKeepsHisBranchOnPaper),
            ("TheHideoutIsOneAddressAndItMoves", TheHideoutIsOneAddressAndItMoves),
            ("TheHideoutGoesWithItsDeed", TheHideoutGoesWithItsDeed),
            ("TheSecondLegRunsOnItsOwnDay", TheSecondLegRunsOnItsOwnDay),
            ("TheVanCanBeTakenLikeTheFirstCar", TheVanCanBeTakenLikeTheFirstCar),
            ("NobodyWalksOutOfACarHeWasNeverIn", NobodyWalksOutOfACarHeWasNeverIn),

            // ------------------------------------------------ GAN-245: the complaint,
            // the trial, the lawyer, bail, the witnesses and the sale
            ("TheBandsAreLonger", TheBandsAreLonger),
            ("EveryDeedHasAnExplicitContract", EveryDeedHasAnExplicitContract),
            ("TheBatteryHasItsOwnTerms", TheBatteryHasItsOwnTerms),
            ("AFoldedCountKeepsItsDeedsWeight", AFoldedCountKeepsItsDeedsWeight),
            ("TheBeatingIsMeasuredBeforeItFrightensHim", TheBeatingIsMeasuredBeforeItFrightensHim),
            ("TheOwnersDocketMakesTheBeatingTampering", TheOwnersDocketMakesTheBeatingTampering),
            ("ABodyOpensOneUncollaredMurderFile", ABodyOpensOneUncollaredMurderFile),
            ("PoliceFireMakesABodyUnattributable", PoliceFireMakesABodyUnattributable),
            ("AnIndoorComplaintHasNoPavementWitnesses", AnIndoorComplaintHasNoPavementWitnesses),
            ("TheDeadOwnerLeavesEveryOpenCase", TheDeadOwnerLeavesEveryOpenCase),
            ("ADeadComplaintCannotBecomeACount", ADeadComplaintCannotBecomeACount),
            ("ABodyStillBecomesOneCountWithoutWitnesses", ABodyStillBecomesOneCountWithoutWitnesses),
            ("UnansweredFilesExpireWithTheirMemory", UnansweredFilesExpireWithTheirMemory),
            ("AHoodGetsLessAndAMarkedLieutenantMore", AHoodGetsLessAndAMarkedLieutenantMore),
            ("ALawyerCutsTheDaysButNotLife", ALawyerCutsTheDaysButNotLife),
            ("AFrightenedOwnerDoesNotRing", AFrightenedOwnerDoesNotRing),
            ("AConnectedOwnerRings", AConnectedOwnerRings),
            ("AStrangerIsRungOnAndAnEstablishedHouseIsNot", AStrangerIsRungOnAndAnEstablishedHouseIsNot),
            ("WordAgainstWordMostlyWalks", WordAgainstWordMostlyWalks),
            ("TwoEyewitnessesConvict", TwoEyewitnessesConvict),
            ("NoWitnessesIsADismissal", NoWitnessesIsADismissal),
            ("RecognitionRecoversTheOpenCase", RecognitionRecoversTheOpenCase),
            ("RecognitionAfterForfeitGetsANewHearing", RecognitionAfterForfeitGetsANewHearing),
            ("BookingEndsThePursuitButKeepsTheCase", BookingEndsThePursuitButKeepsTheCase),
            ("LegacyCourtOutcomesEndThePursuit", LegacyCourtOutcomesEndThePursuit),
            ("ThePoliceWhoSawItAreNotSilenced", ThePoliceWhoSawItAreNotSilenced),
            ("AWithdrawnWitnessIsOffTheCase", AWithdrawnWitnessIsOffTheCase),
            ("AnOpenComplaintIsAnExtraCount", AnOpenComplaintIsAnExtraCount),
            ("EveryCloseWritesAVerdict", EveryCloseWritesAVerdict),
            ("AFoldedCaseIsNotATrial", AFoldedCaseIsNotATrial),
            ("ASkippedManLapsesOffTheDocket", ASkippedManLapsesOffTheDocket),
            ("TheDocketListsEveryOpenCaseOfOurs", TheDocketListsEveryOpenCaseOfOurs),
            ("TheReadIsTakenOnTheWitnessesTheCourtWillHear",
                TheReadIsTakenOnTheWitnessesTheCourtWillHear),
            ("TheSheetAndTheFileUseOneWord", TheSheetAndTheFileUseOneWord),
            ("TheArchiveReadsNewestFirst", TheArchiveReadsNewestFirst),
            ("BailComesBackAsAMan", BailComesBackAsAMan),
            ("SkippedBailIsWTwoAndTheMoneyIsGone", SkippedBailIsWTwoAndTheMoneyIsGone),
            ("CutLooseCostsTheCrewMost", CutLooseCostsTheCrewMost),
            ("StandingByHimPaysAPointAWeek", StandingByHimPaysAPointAWeek),
            ("ARearrestPutsABailedManBack", ARearrestPutsABailedManBack),
            ("CuttingLooseTheLastDefendantClosesTheCase",
                CuttingLooseTheLastDefendantClosesTheCase),
            ("EveryManTriedOnPaperIsReported", EveryManTriedOnPaperIsReported),
            ("ARivalPrisonerKeepsHisHouseThroughTheVerdict",
                ARivalPrisonerKeepsHisHouseThroughTheVerdict),
            ("TwoFailedTransfersPutHimBeforeTheJudgeOnPaper",
                TwoFailedTransfersPutHimBeforeTheJudgeOnPaper),
        };

        // ------------------------------------------------------------------- AI-006

        /// <summary>
        /// A16. A transfer that failed to run TransferFailsBeforePaper days running is
        /// carried on paper: the man is put in front of the judge without a car, gets
        /// the same verdict he would off a convoy, and the same rule then carries the
        /// van to the prison. Before that many failures he simply waits, as he did.
        /// </summary>
        static void TwoFailedTransfersPutHimBeforeTheJudgeOnPaper(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 10);
            var wanted = new List<Prisoner>();
            var paper = new List<Prisoner>();

            var courtDay = 10 + Sentencing.DaysToCourt;
            var day = courtDay;
            for (var fail = 0; fail < PrisonPipeline.TransferFailsBeforePaper; fail++)
            {
                pipe.DayTick(day, wanted, paper);
                Want(failures, wanted.Count == 1 && paper.Count == 0,
                    "PIPE-006: he rides a car while the road has failed him only " + fail +
                    " times.");
                pipe.BackToTheCells(prisoner, day);
                day++;
            }

            pipe.DayTick(day, wanted, paper);
            Want(failures, wanted.Count == 0 && paper.Count == 1,
                "PIPE-006: after " + PrisonPipeline.TransferFailsBeforePaper +
                " failed days the leg did not go on paper.");
            Want(failures, prisoner.Leg == PrisonLeg.Court,
                "PIPE-006: the paper leg is not the court leg.");

            pipe.OnPaper(roster, prisoner, day);
            Want(failures, prisoner.Stage == PrisonStage.Sentenced ||
                           prisoner.Stage == PrisonStage.Cleared,
                "PIPE-006: the paper transfer did not put him before a judge (" +
                prisoner.Stage + ").");
            Want(failures, prisoner.TransferFails == 0,
                "PIPE-006: a verdict did not reset the failed-days count.");

            if (prisoner.Stage != PrisonStage.Sentenced)
                return;

            // The second leg, the same rule.
            day = prisoner.PrisonDay;
            for (var fail = 0; fail < PrisonPipeline.TransferFailsBeforePaper; fail++)
            {
                pipe.DayTick(day, wanted, paper);
                Want(failures, wanted.Count == 1 && paper.Count == 0,
                    "PIPE-006: the van still runs while it has failed only " + fail +
                    " times.");
                pipe.BackToTheCells(prisoner, day);
                day++;
            }
            pipe.DayTick(day, wanted, paper);
            Want(failures, paper.Count == 1 && prisoner.Leg == PrisonLeg.Prison,
                "PIPE-006: the prison leg did not go on paper after two failed days.");
            pipe.OnPaper(roster, prisoner, day);
            Want(failures, prisoner.Stage == PrisonStage.Serving,
                "PIPE-006: the paper van did not deliver him (" + prisoner.Stage + ").");

            // A caller that hands no paper list gets the old behaviour exactly.
            var old = BookedRoster(out var other, out var oldPipe);
            var held = oldPipe.Book(old, other.Id, Deed.Affray, 10);
            held.TransferFails = 99;
            oldPipe.DayTick(10 + Sentencing.DaysToCourt, wanted);
            Want(failures, wanted.Count == 1,
                "PIPE-006: with no paper list the man was neither sent nor listed.");
        }

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

        static void Want(List<string> failures, bool condition, string what)
        {
            if (!condition)
                failures.Add(what);
        }

        // ------------------------------------------------------------ the gunpoint ask

        static void NerveAndTemperDecideTheAnswer(List<string> failures)
        {
            var coward = SurrenderRoll.FightChance(5, 20, 90);
            var middling = SurrenderRoll.FightChance(50, 50, 50);
            var hothead = SurrenderRoll.FightChance(95, 95, 10);

            Want(failures, coward < middling && middling < hothead,
                "SURRENDER: the odds must rise with nerve and temper (" +
                coward + " / " + middling + " / " + hothead + ").");
            Want(failures, coward >= SurrenderRoll.Floor && hothead <= SurrenderRoll.Ceiling,
                "SURRENDER: the band must stay inside its floor and ceiling.");
            Want(failures,
                SurrenderRoll.FightChance(50, 50, 90) < SurrenderRoll.FightChance(50, 50, 10),
                "SURRENDER: men who trust the outfit go quietly - loyalty must subtract.");
        }

        static void TheAnswerIsTheSameTwiceForOneSeed(List<string> failures)
        {
            var stream = SurrenderRoll.StreamFor(1987, 4, 11);
            Want(failures,
                SurrenderRoll.Fights(0.5f, stream) == SurrenderRoll.Fights(0.5f, stream),
                "SURRENDER: one stream must answer the same twice.");
            Want(failures, SurrenderRoll.StreamFor(1987, 4, 11) != SurrenderRoll.StreamFor(1987, 4, 12),
                "SURRENDER: two incidents must not share a crew's stream.");
            Want(failures, SurrenderRoll.StreamFor(1987, 4, 11) != SurrenderRoll.StreamFor(1987, 5, 11),
                "SURRENDER: two crews must not share an incident's stream.");

            // and the odds have to actually govern: a floor of certainty each way
            Want(failures, SurrenderRoll.Fights(1f, stream), "SURRENDER: certainty must fight.");
            Want(failures, !SurrenderRoll.Fights(0f, stream), "SURRENDER: nought must not fight.");
        }

        static void TheLeaningIsReadableWithoutANumber(List<string> failures)
        {
            Want(failures, SurrenderRoll.Leaning(0.1f).Length > 0 &&
                           SurrenderRoll.Leaning(0.9f).Length > 0,
                "SURRENDER: the banner must have words for both ends of the band.");
            Want(failures, SurrenderRoll.Leaning(0.1f) != SurrenderRoll.Leaning(0.9f),
                "SURRENDER: a quiet crew and a hot one must not read the same.");
        }

        static void ThreeAnswersUseOneDeterministicStream(List<string> failures)
        {
            var stream = SurrenderRoll.StreamFor(1987, 14, 8);
            Want(failures,
                SurrenderRoll.Answer(0f, 0f, true, stream) == DoorAnswer.Quiet,
                "ANSWER: a zero refusal chance must go quietly.");
            Want(failures,
                SurrenderRoll.Answer(1f, 0f, true, stream) == DoorAnswer.Run,
                "ANSWER: a refusal below the fight draw must run.");
            Want(failures,
                SurrenderRoll.Answer(1f, 1f, true, stream) == DoorAnswer.Fight,
                "ANSWER: an armed refusal above the fight draw must fight.");
            Want(failures,
                SurrenderRoll.Answer(1f, 1f, false, stream) == DoorAnswer.Run,
                "ANSWER: an unarmed crew can only run when it refuses.");
            var once = SurrenderRoll.Answer(0.41f, 0.63f, true, stream);
            var twice = SurrenderRoll.Answer(0.41f, 0.63f, true, stream);
            Want(failures, once == twice,
                "ANSWER: one crew and incident must consume the same two draws twice.");
            const float legacyChance = 0.41f;
            var legacyRefused = SurrenderRoll.Fights(legacyChance, stream);
            Want(failures,
                (SurrenderRoll.Answer(legacyChance, 0.63f, true, stream) != DoorAnswer.Quiet) ==
                legacyRefused,
                "ANSWER: draw one must preserve the old seeded refusal decision.");
            Want(failures,
                SurrenderRoll.FightAfterRefusal(90, 80, 10) >
                SurrenderRoll.FightAfterRefusal(10, 20, 90),
                "ANSWER: temper and courage must beat discipline in the second draw.");
            Want(failures,
                SurrenderRoll.MostSerious(DoorAnswer.Run, DoorAnswer.Quiet) ==
                DoorAnswer.Run &&
                SurrenderRoll.MostSerious(DoorAnswer.Run, DoorAnswer.Fight) ==
                DoorAnswer.Fight,
                "ANSWER: capture later must not soften a run or a fight already on file.");
        }

        static void InCustodyRefusesEveryOrder(List<string> failures)
        {
            Want(failures, CustodyPlan.RefusesOrders(true) &&
                           !CustodyPlan.RefusesOrders(false),
                "CUSTODY: the shared gate must refuse every order only while held.");
        }

        static void CarsForPrisoners(List<string> failures)
        {
            Want(failures,
                CustodyPlan.PickupOccupantLimit == 8 &&
                CustodyPlan.EscortSeats + CustodyPlan.PrisonersPerPickup == 8,
                "CUSTODY: a pickup must carry no more than eight people including its escort.");
            Want(failures, CustodyPlan.CarsForPrisoners(5, 4) == 1,
                "CUSTODY: one pickup must take a whole five-man street crew.");
            Want(failures, CustodyPlan.CarsForPrisoners(8, 3) == 2,
                "CUSTODY: even a large arrest must leave one on-duty car free.");
            Want(failures, CustodyPlan.CarsForPrisoners(2, 1) == 1,
                "CUSTODY: the last free car must answer the custody already waiting.");
            Want(failures, CustodyPlan.PrisonersThisTrip(6, 1) == 6,
                "CUSTODY: six prisoners must fit behind one pickup's two officers.");
            Want(failures, CustodyPlan.PrisonersThisTrip(7, 1) == 6,
                "CUSTODY: one pickup must enforce its eight-person limit.");
            Want(failures, CustodyPlan.PrisonersThisTrip(1, 2) == 1,
                "CUSTODY: the return trip must carry only the man left at the pickup.");
        }

        // ------------------------------------------------ GAN-315: user's exact live repro

        static void GAN315_ArrestedManRaisesHisHands(List<string> failures)
        {
            Want(failures,
                CustodyPlan.ShouldRaiseHands(surrendered: true, riding: false, moving: false),
                "GAN-315/1: a stationary arrested man must show the hands-up loop.");
            Want(failures,
                !CustodyPlan.ShouldRaiseHands(true, riding: true, moving: false) &&
                !CustodyPlan.ShouldRaiseHands(true, riding: false, moving: true),
                "GAN-315/1: the static loop must yield to the escorted walk and car seat.");
        }

        static void GAN315_PoliceKeepEveryPrisonerCovered(List<string> failures)
        {
            Want(failures,
                CustodyPlan.MustCoverPrisoner(inCustody: true, booked: false, riding: false),
                "GAN-315/2: an unbooked prisoner outside the car must stay at gunpoint.");
            Want(failures,
                !CustodyPlan.MustCoverPrisoner(true, booked: true, riding: false) &&
                !CustodyPlan.MustCoverPrisoner(true, booked: false, riding: true),
                "GAN-315/2: cover ends only after the man is seated or booked.");
        }

        static void GAN315_PrisonerBoardsOnlyWithHisEscort(List<string> failures)
        {
            Want(failures,
                CustodyPlan.CanSeatPrisoner(atRearDoor: true, escortBesideHim: true) &&
                !CustodyPlan.CanSeatPrisoner(atRearDoor: true, escortBesideHim: false) &&
                !CustodyPlan.CanSeatPrisoner(atRearDoor: false, escortBesideHim: true),
                "GAN-315/3: no prisoner may enter the car without a physical escort.");
        }

        static void GAN315_NormalBoardingNeverSpringsCustody(List<string> failures)
        {
            Want(failures,
                !CustodyPlan.ShouldSpring(carrierWrecked: false, escortWiped: false),
                "GAN-315/5: ordinary actor spacing must never produce SPRUNG or restore control.");
            Want(failures,
                CustodyPlan.ShouldSpring(carrierWrecked: true, escortWiped: false) &&
                CustodyPlan.ShouldSpring(carrierWrecked: false, escortWiped: true),
                "GAN-315/5: only a wreck or a wiped escort may break physical custody.");
        }

        static void GAN315_RightClickCannotReleaseAPrisoner(List<string> failures)
        {
            Want(failures,
                CustodyPlan.RefusesOrders(inCustody: true),
                "GAN-315/5: right-click movement, car-exit and attack orders must all stay refused in custody.");
        }

        static void GAN315_OfficerReturnsToTheCarPromptly(List<string> failures)
        {
            Want(failures, PoliceProcedure.OfficerBoardingSeconds <= 8f,
                "GAN-315/4: an officer may not wander for thirty seconds before boarding.");
        }

        static void GAN315_ShopStatementRequiresARealEntry(List<string> failures)
        {
            Want(failures,
                PoliceProcedure.CanRecordShopStatement(
                    crossedThreshold: true, completedInterview: true) &&
                !PoliceProcedure.CanRecordShopStatement(false, true) &&
                !PoliceProcedure.CanRecordShopStatement(true, false),
                "GAN-315/statement: no case or statement may be filed by an officer passing outside.");
        }

        static void GAN315_OneUniformAndAFastDispatch(List<string> failures)
        {
            Want(failures,
                PoliceProcedure.UniformOfficerPrefabName == "SM_Chr_Officer_Male_01",
                "GAN-315/uniform: every patrol and response squad must use the canonical officer.");
            Want(failures,
                PoliceProcedure.ComplaintDelayMinimum >= 0f &&
                PoliceProcedure.ComplaintDelayMaximum <= 4f &&
                PoliceProcedure.ComplaintDelayMinimum < PoliceProcedure.ComplaintDelayMaximum,
                "GAN-315/response: a shop call must leave dispatch inside four seconds.");
        }

        static void GAN315_DispatchUsesAirDistanceNotTravelTime(List<string> failures)
        {
            var footAcrossTheRoad = PoliceProcedure.AirDistanceSquared(
                ax: 20f, az: 0f, bx: 0f, bz: 0f);
            var fasterCarFartherAway = PoliceProcedure.AirDistanceSquared(
                ax: 40f, az: 0f, bx: 0f, bz: 0f);
            var footTravelTime = Math.Sqrt(footAcrossTheRoad) / 2.6;
            var carTravelTime = Math.Sqrt(fasterCarFartherAway) / 8.0;

            Want(failures,
                footAcrossTheRoad < fasterCarFartherAway &&
                carTravelTime < footTravelTime,
                "GAN-315/dispatch: the nearer foot patrol must win even when a farther car has a shorter ETA.");
            Want(failures,
                PoliceProcedure.AirDistanceSquared(3f, 4f, 0f, 0f) == 25f,
                "GAN-315/dispatch: nearest must be the direct overhead-map chord.");
        }

        static void GAN315_ComplaintWaitsForPhysicalArrival(List<string> failures)
        {
            Want(failures,
                !PoliceProcedure.CanProcessComplaintArrival(unitOnScene: false) &&
                PoliceProcedure.CanProcessComplaintArrival(unitOnScene: true),
                "GAN-315/arrest: entering the shop's search radius must not skip the actual on-scene arrival.");
        }

        /// <summary>The user's rule of 2026-09-04: the nearest pair comes wherever it
        /// is; past 150 m a car goes out beside it; whoever arrives first arrests.</summary>
        static void TheNearestPairComesAndACarPastOneFifty(List<string> failures)
        {
            var near = 100f * 100f;
            var far = 151f * 151f;
            Want(failures,
                !PoliceProcedure.CarJoinsFootResponse(anyFootFree: true, near) &&
                PoliceProcedure.CarJoinsFootResponse(anyFootFree: true, far),
                "RESPONSE: a pair inside 150 m goes alone; past it a car goes out beside him.");
            Want(failures,
                PoliceProcedure.CarJoinsFootResponse(anyFootFree: false, 0f),
                "RESPONSE: nobody free on foot sends the car by itself.");
            Want(failures,
                PoliceProcedure.FootArrivedFirst(10f, 12f) &&
                !PoliceProcedure.FootArrivedFirst(12f, 10f) &&
                PoliceProcedure.FootArrivedFirst(10f, 10f),
                "RESPONSE: whoever arrived first makes the arrest, a tie to the men on foot.");
        }

        static void EveryNearbyPatrolJoinsAndDispatchSendsAtMostOneCar(
            List<string> failures)
        {
            var inside = PoliceProcedure.NearbyPoliceGunfightRange - 1f;
            var edge = PoliceProcedure.NearbyPoliceGunfightRange;
            var outside = PoliceProcedure.NearbyPoliceGunfightRange + 1f;
            Want(failures,
                PoliceProcedure.NearbyPoliceJoinsGunfight(true, inside * inside) &&
                PoliceProcedure.NearbyPoliceJoinsGunfight(true, edge * edge) &&
                !PoliceProcedure.NearbyPoliceJoinsGunfight(true, outside * outside) &&
                !PoliceProcedure.NearbyPoliceJoinsGunfight(false, 0f),
                "GUNFIGHT RESPONSE: every free foot or motor patrol in earshot joins; a distant or occupied patrol does not.");

            Want(failures,
                PoliceProcedure.OrdinaryDispatchedCars(true, 0, true) == 1 &&
                PoliceProcedure.OrdinaryDispatchedCars(true, 5, true) == 1 &&
                PoliceProcedure.OrdinaryDispatchedCars(false, 2, true) == 1 &&
                PoliceProcedure.OrdinaryDispatchedCars(false, 0, false) == 1 &&
                PoliceProcedure.OrdinaryDispatchedCars(false, 0, true) == 0 &&
                PoliceProcedure.OrdinaryDispatchCarStillAllowed(0) &&
                !PoliceProcedure.OrdinaryDispatchCarStillAllowed(1) &&
                !PoliceProcedure.OrdinaryDispatchCarStillAllowed(2),
                "GUNFIGHT RESPONSE: dispatch calls no more than one car; only a quiet low-heat scene with a free pair calls none.");
        }

        static void PoliceFireCanBeAnsweredWithoutSummoningTheSwarm(
            List<string> failures)
        {
            var defensive = PoliceProcedure.IsDefensivePoliceReturn(17, 17);
            Want(failures,
                defensive &&
                !PoliceProcedure.IsDefensivePoliceReturn(16, 17) &&
                !PoliceProcedure.IsDefensivePoliceReturn(-1, 17) &&
                PoliceProcedure.PoliceInterventionCreatesDefence(
                    policeFiredAtCrew: true, crewWasFightingNonPolice: true) &&
                !PoliceProcedure.PoliceInterventionCreatesDefence(
                    policeFiredAtCrew: true, crewWasFightingNonPolice: false) &&
                !PoliceProcedure.PoliceInterventionCreatesDefence(
                    policeFiredAtCrew: false, crewWasFightingNonPolice: true) &&
                PoliceProcedure.CrewMayAnswerAttacker(
                    attackerIsPolice: true, policeOpenedFireThisIncident: true) &&
                !PoliceProcedure.CrewMayAnswerAttacker(
                    attackerIsPolice: true, policeOpenedFireThisIncident: false) &&
                PoliceProcedure.CrewMayAnswerAttacker(
                    attackerIsPolice: false, policeOpenedFireThisIncident: false),
                "POLICE RETURN FIRE: only a police attack in this same shooting incident makes the reply defensive.");
            Want(failures,
                !PoliceProcedure.ShotAtPoliceStartsSwarm(true, defensive) &&
                PoliceProcedure.ShotAtPoliceStartsSwarm(true, defensiveReturn: false) &&
                !PoliceProcedure.ShotAtPoliceStartsSwarm(false, defensiveReturn: false),
                "POLICE RETURN FIRE: self-defence stays local, while opening fire on police still summons the swarm.");
        }

        /// <summary>A pair that stops getting nearer is stood at the scene if it is within
        /// a street of it, and sent back - the next pair sent - if it is not.</summary>
        static void AStalledPairIsAtTheSceneOrSentBack(List<string> failures)
        {
            Want(failures,
                !PoliceProcedure.StalledOnTheWay(24f) && PoliceProcedure.StalledOnTheWay(26f),
                "RESPONSE: a pair is stuck after 25 s without a metre of progress, not before.");
            Want(failures,
                PoliceProcedure.StalledPairIsAtTheScene(45f) &&
                !PoliceProcedure.StalledPairIsAtTheScene(46f),
                "RESPONSE: a stuck pair inside 45 m is at the scene; farther out it is sent back.");
        }

        static void GAN315_BoardingDoesNotResetALiveRoute(List<string> failures)
        {
            Want(failures,
                !CustodyPlan.ShouldRetryBoarding(
                    hasOrder: true, atDestination: false, retryElapsed: true) &&
                !CustodyPlan.ShouldRetryBoarding(
                    hasOrder: false, atDestination: true, retryElapsed: true) &&
                !CustodyPlan.ShouldRetryBoarding(
                    hasOrder: false, atDestination: false, retryElapsed: false) &&
                CustodyPlan.ShouldRetryBoarding(
                    hasOrder: true, atDestination: false, retryElapsed: true,
                    routeStalled: true) &&
                CustodyPlan.ShouldRetryBoarding(
                    hasOrder: false, atDestination: false, retryElapsed: true),
                "GAN-315/boarding: retry only an idle or genuinely stalled route, never one still progressing.");
        }

        static void GAN315_EscortStandsClearAndTheCarParks(List<string> failures)
        {
            Want(failures,
                PoliceProcedure.CustodyEscortCarClearance >= 3f,
                "GAN-315/escort: the covering officer must stand clear of the prisoner's car door.");
            Want(failures,
                PoliceProcedure.ResponseCarsParkAtKerb &&
                PoliceProcedure.CustodyCarStandOff <= 3f,
                "GAN-315/car: the nearest carrier must park at the kerb close to the pickup.");
            Want(failures,
                !PoliceProcedure.ResponseCarArrived(
                    goalComplete: true, parkedAtKerb: false) &&
                !PoliceProcedure.ResponseCarArrived(
                    goalComplete: false, parkedAtKerb: true) &&
                PoliceProcedure.ResponseCarArrived(
                    goalComplete: true, parkedAtKerb: true),
                "GAN-315/car: a lane stop is not arrival, while a completed real kerb park is.");
        }

        static void GAN315_TransferTracksOnlyPhysicalCustody(List<string> failures)
        {
            Want(failures,
                !CustodyPlan.CanBook(crossedStationThreshold: false) &&
                CustodyPlan.CanBook(crossedStationThreshold: true),
                "GAN-315/HUD: a timeout at the car may not remove the prisoner before the station threshold.");

            var bailRoster = BookedRoster(out var bailedMan, out var bailPipe);
            var bailed = bailPipe.Book(bailRoster, bailedMan.Id, Deed.Affray, 10);
            var trackedWhileHeld = bailed != null &&
                CustodyPlan.TracksStage(bailed.Stage);
            var bailReleased = bailPipe.PostBail(bailRoster, bailed,
                PrisonPipeline.BailPrice(bailed), 10);

            var courtRoster = BookedRoster(out var clearedMan, out var courtPipe);
            var emptyCase = courtPipe.OpenCase(Deed.Extortion, 0, 10, 15,
                "shop-release", "THE SHOP");
            var cleared = courtPipe.Book(courtRoster, clearedMan.Id,
                Deed.Extortion, 10, emptyCase);
            courtPipe.Tried(courtRoster, cleared, 15);

            var wreckRoster = BookedRoster(out var freedMan, out var wreckPipe);
            var freed = wreckPipe.Book(wreckRoster, freedMan.Id, Deed.Murder, 10);
            var transfer = new List<Prisoner>();
            wreckPipe.DayTick(freed.CourtDay, transfer);
            wreckPipe.Away(freed);
            wreckPipe.Freed(wreckRoster, freed, freed.CourtDay);

            Want(failures,
                trackedWhileHeld && bailReleased &&
                bailed.Stage == PrisonStage.Bailed &&
                !CustodyPlan.TracksStage(bailed.Stage) &&
                cleared.Stage == PrisonStage.Cleared &&
                !CustodyPlan.TracksStage(cleared.Stage) &&
                freed.Stage == PrisonStage.Freed &&
                !CustodyPlan.TracksStage(freed.Stage),
                "GAN-315/HUD: tracking must last through physical custody and end on every street-release stage.");
        }

        static void GAN315_ResponseRunsAndFlightDrawsFire(List<string> failures)
        {
            Want(failures, PoliceProcedure.RunToScene,
                "GAN-315/response: officers must run the shared crew route to the scene.");
            Want(failures,
                PoliceProcedure.ShouldOpenFireOnFlight(
                    arrestInProgress: true, suspectMoved: true) &&
                !PoliceProcedure.ShouldOpenFireOnFlight(true, false) &&
                !PoliceProcedure.ShouldOpenFireOnFlight(false, true),
                "GAN-315/gunpoint: movement during a live arrest must turn cover into fire.");
        }

        // ------------------------------------------------ EPIC 35: road to the courthouse

        static void ROAD000_BlastAccountingDoesNotDoubleCount(List<string> failures)
        {
            Want(failures,
                CustodyPlan.FallbackOfficerDeaths(0) == 2 &&
                CustodyPlan.FallbackOfficerDeaths(1) == 0 &&
                CustodyPlan.FallbackOfficerDeaths(2) == 0,
                "ROAD-000: the two decree deaths exist only when no physical escort body can report itself.");
        }

        static void ROAD001_TheCarriageNeedsPhysicalEdges(List<string> failures)
        {
            Want(failures,
                CustodyPlan.ShouldHalt(CarriageStage.Riding,
                    prisonerSeated: true, firstRoundIntoTin: true) &&
                !CustodyPlan.ShouldHalt(CarriageStage.Calling, true, true) &&
                !CustodyPlan.ShouldHalt(CarriageStage.Riding, false, true) &&
                !CustodyPlan.ShouldHalt(CarriageStage.Riding, true, false),
                "ROAD-001: only the first round into a physically loaded ride may halt it.");
            Want(failures,
                CustodyPlan.ShouldDismount(CarriageStage.Halted,
                    carrierStopped: true) &&
                !CustodyPlan.ShouldDismount(CarriageStage.Halted, false) &&
                !CustodyPlan.ShouldDismount(CarriageStage.Riding, true),
                "ROAD-001: bodies leave their seats only after the carrier has stopped.");
            Want(failures,
                !CustodyPlan.CanDeliver(CarriageStage.Riding,
                    thresholdCrossed: false, countyLineLeg: false) &&
                CustodyPlan.CanDeliver(CarriageStage.WalkingIn,
                    thresholdCrossed: true, countyLineLeg: false) &&
                CustodyPlan.CanDeliver(CarriageStage.Riding,
                    thresholdCrossed: false, countyLineLeg: true),
                "ROAD-001: court delivery is a walked threshold; the prison leg is the county line.");
            Want(failures,
                (int)CarriageStage.Calling == 0 &&
                (int)CarriageStage.WalkingOut == 1 &&
                (int)CarriageStage.Boarding == 2 &&
                (int)CarriageStage.Riding == 3 &&
                (int)CarriageStage.Halted == 4 &&
                (int)CarriageStage.WalkingIn == 5 &&
                (int)CarriageStage.Delivered == 6 &&
                (int)CaseOutcome.Convicted == 0 &&
                (int)CaseOutcome.Acquitted == 1 &&
                (int)CaseOutcome.Dismissed == 2 &&
                (int)CaseOutcome.BailForfeit == 3 &&
                (int)CaseOutcome.CutLoose == 4 &&
                (int)CaseOutcome.Killed == 5,
                "ROAD-001/002: saved case outcomes retain their old ordinals and carriage stages are append-only.");
        }

        static void ROAD002_AKilledPrisonerClosesHisCase(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = pipe.OpenCase(Deed.Murder, 0, 10,
                10 + Sentencing.DaysToCourt, "road-12", "THE ROAD");
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10, file);
            var due = new List<Prisoner>();
            pipe.DayTick(prisoner.CourtDay, due);
            pipe.Away(prisoner);

            var killed = pipe.Killed(roster, man.Id, prisoner.CourtDay);
            var verdict = file.VerdictFor(man.Id);
            Want(failures,
                killed == prisoner && pipe.Find(man.Id) == null &&
                prisoner.Stage == PrisonStage.Cleared && prisoner.Leg == PrisonLeg.None,
                "ROAD-002: a dead rider leaves the prison pipeline without becoming freed.");
            Want(failures,
                verdict != null && verdict.Outcome == CaseOutcome.Killed &&
                file.Verdicts.Count == 1 && !file.HasDefendant(man.Id) &&
                file.Status == CaseStatus.Folded,
                "ROAD-002: his open prosecution closes as killed, not acquitted or tried.");
            Want(failures,
                pipe.Freed(roster, prisoner, prisoner.CourtDay) == null &&
                file.Verdicts.Count == 1,
                "ROAD-002: a killed prisoner cannot later be freed or earn a second verdict.");
            Want(failures,
                man.Status == CharacterStatus.Jailed &&
                man.RapSheet[man.RapSheet.Count - 1].Outcome ==
                    Sentencing.KilledInTransferOutcome,
                "ROAD-002: the pipeline writes the outcome but leaves roster death to the shared street channel.");
        }

        static void ROAD003_JeopardyIsCapped(List<string> failures)
        {
            Want(failures,
                CustodyPlan.OccupantHitChance > 0.16f &&
                CustodyPlan.OccupantHitChance < 0.17f,
                "ROAD-003: one ordinary halted engagement carries roughly a one-in-six occupant risk.");
            Want(failures,
                CustodyPlan.InJeopardy(CarriageStage.Halted, prisonerSeated: true,
                    secondsSinceLastRoll: CustodyPlan.OccupantRollInterval, rolls: 0) &&
                !CustodyPlan.InJeopardy(CarriageStage.Riding, true, 10f, 0) &&
                !CustodyPlan.InJeopardy(CarriageStage.Halted, false, 10f, 0) &&
                !CustodyPlan.InJeopardy(CarriageStage.Halted, true,
                    CustodyPlan.OccupantRollInterval - 0.01f, 0) &&
                !CustodyPlan.InJeopardy(CarriageStage.Halted, true, 10f,
                    CustodyPlan.MaxOccupantRolls),
                "ROAD-003: friendly-fire risk is seated, timed and capped per engagement.");
        }

        static void ROAD003_PreSeatAmbushIsASpring(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = pipe.OpenCase(Deed.Affray, 0, 10,
                10 + Sentencing.DaysToCourt, "road-pre-seat", "THE STATION DOOR");
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 10, file);
            var due = new List<Prisoner>();
            pipe.DayTick(prisoner.CourtDay, due);

            Want(failures, prisoner.Stage == PrisonStage.ForTransfer &&
                           pipe.Freed(roster, prisoner, prisoner.CourtDay) == null,
                "ROAD-003: Freed must refuse a man who has not sat in the carrier.");
            Want(failures, pipe.Sprung(roster, man.Id, prisoner.CourtDay),
                "ROAD-003: a foot ambush before seating must use the sprung exit.");
            Want(failures, pipe.Find(man.Id) == null &&
                           prisoner.Stage == PrisonStage.Freed && prisoner.Sprung &&
                           man.Status == CharacterStatus.Active &&
                           man.WantedLevel == WantedLevels.FreedFromTransfer,
                "ROAD-003: the sprung man must be active, out of the pipe and W2.");
            Want(failures, file.Status == CaseStatus.Open &&
                           file.Verdicts.Count == 0 &&
                           file.ExtraCharges.Contains(Deed.Resisting),
                "ROAD-003: springing leaves the case open with a resisting count.");
        }

        static void ROAD004_TheWalkHomeIsBounded(List<string> failures)
        {
            Want(failures,
                CustodyPlan.WalkTheRest(freshCarrierAvailable: false,
                    metresRemaining: CustodyPlan.WalkTheRestLimit) &&
                !CustodyPlan.WalkTheRest(false,
                    CustodyPlan.WalkTheRestLimit + 0.01f) &&
                !CustodyPlan.WalkTheRest(true, 20f),
                "ROAD-004: a short escorted leg is allowed only when no fresh carrier exists.");
            Want(failures,
                LivingCity.UI.LedgerText.CarriageStageLabel(
                    CarriageStage.Halted, PrisonLeg.Court) ==
                    "the transfer is halted" &&
                LivingCity.UI.LedgerText.CarriageStageLabel(
                    CarriageStage.Riding, PrisonLeg.Prison) ==
                    "in the van out of town",
                "ROAD-005: THE LAW reads the shared carriage stages in words.");
        }

        static void ROAD004_DeadTinLeavesTheFleetBook(List<string> failures)
        {
            Want(failures,
                PoliceFleet.CountsAsBody(wrecked: false, engineDead: false,
                    retiredDerelict: false) &&
                !PoliceFleet.CountsAsBody(wrecked: true, engineDead: false,
                    retiredDerelict: false) &&
                !PoliceFleet.CountsAsBody(wrecked: false, engineDead: true,
                    retiredDerelict: false) &&
                !PoliceFleet.CountsAsBody(wrecked: false, engineDead: false,
                    retiredDerelict: true),
                "ROAD-004: wrecks, shot-out engines and halted derelicts must not occupy fleet slots.");
        }

        static void ROAD004_ExceptionalStagesHaveBackstops(List<string> failures)
        {
            Want(failures,
                CustodyPlan.StrandedBackstopSeconds > 300f &&
                CustodyPlan.WalkingBackstopSeconds > 300f &&
                CustodyPlan.CourtExitBackstopSeconds > 0f &&
                CustodyPlan.StrandedBackstopSeconds < float.PositiveInfinity &&
                CustodyPlan.WalkingBackstopSeconds < float.PositiveInfinity &&
                CustodyPlan.CourtExitBackstopSeconds < float.PositiveInfinity,
                "ROAD-004: hostile recovery, threshold walking and court exit need finite, explicit ceilings.");
            Want(failures,
                !CustodyPlan.BackstopExpired(899.99f, 900f) &&
                CustodyPlan.BackstopExpired(900f, 900f) &&
                !CustodyPlan.BackstopExpired(900f, float.PositiveInfinity),
                "ROAD-004: an absolute state deadline expires once and cannot be an infinite retry clock.");
        }

        static void ROAD006_ALiveJourneySavesAtItsSource(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var court = pipe.Book(roster, man.Id, Deed.Affray, 10);
            var due = new List<Prisoner>();
            pipe.DayTick(court.CourtDay, due);
            pipe.Away(court);
            court.Carriage = CarriageStage.Riding;

            var rows = PrisonSnapshot.Prisoners(pipe, court.CourtDay);
            Want(failures,
                rows.Length == 1 &&
                rows[0].stage == (int)PrisonStage.Held &&
                rows[0].leg == (int)PrisonLeg.None &&
                rows[0].courtDay == court.CourtDay + 1,
                "ROAD-006: a saved court ride returns to the cells on tomorrow's sheet.");

            // The second leg has a different physical source: after sentence he waits
            // in court custody, and a lost runtime carriage must not erase that verdict.
            var secondRoster = BookedRoster(out var secondMan, out var secondPipe);
            var prison = secondPipe.Book(secondRoster, secondMan.Id, Deed.Murder, 20);
            secondPipe.DayTick(prison.CourtDay, due);
            secondPipe.Away(prison);
            secondPipe.Convicted(secondRoster, prison, prison.CourtDay);
            secondPipe.DayTick(prison.PrisonDay, due);
            secondPipe.Away(prison);
            prison.Carriage = CarriageStage.Halted;

            var prisonRows = PrisonSnapshot.Prisoners(secondPipe, prison.PrisonDay);
            Want(failures,
                prisonRows.Length == 1 &&
                prisonRows[0].stage == (int)PrisonStage.Sentenced &&
                prisonRows[0].leg == (int)PrisonLeg.None &&
                prisonRows[0].prisonDay == prison.PrisonDay + 1,
                "ROAD-006: a saved prison ride returns to court custody without losing its sentence.");
        }

        // ------------------------------------------------------------------ the roster

        static void ARosterNeverGoesAboveItsStrength(List<string> failures)
        {
            var config = new PoliceRosterConfig();
            var roster = new PoliceRoster(0, "Precinct 1", 3, 6);

            Want(failures, roster.Cars == 3 && roster.Officers == 6,
                "ROSTER: a fresh precinct stands at its authorised strength.");

            for (var i = 0; i < 10; i++) roster.Lose(PoliceLoss.Officer, 5, config);
            Want(failures, roster.Officers == 0,
                "ROSTER: strength must not go negative however many deaths are heard.");
            Want(failures, roster.Missing(PoliceLoss.Officer) == 6,
                "ROSTER: only the men it actually had can be lost.");

            roster.Replace(99, null);
            Want(failures, roster.Officers == 6 && roster.Cars == 3,
                "ROSTER: replacement must never take a precinct above its authorised strength.");
        }

        static void AHoleIsFilledOnItsOwnDayAndNoSooner(List<string> failures)
        {
            var config = new PoliceRosterConfig { OfficerDays = 2, CarDays = 3 };
            var roster = new PoliceRoster(0, "Precinct 1", 2, 2);
            var filled = new List<PoliceLossRecord>();

            var man = roster.Lose(PoliceLoss.Officer, 10, config);
            var car = roster.Lose(PoliceLoss.Car, 10, config);
            Want(failures, man != null && man.BackOnDay == 12,
                "ROSTER: an officer's replacement day is an ABSOLUTE day two on.");
            Want(failures, car != null && car.BackOnDay == 13,
                "ROSTER: a car's replacement day is an ABSOLUTE day three on.");

            Want(failures, roster.Replace(11, filled) == 0 && roster.Officers == 1,
                "ROSTER: nothing is filled before its day.");
            Want(failures, roster.Replace(12, filled) == 1 && filled[0].Kind == PoliceLoss.Officer,
                "ROSTER: the man comes back on his day and the car does not.");
            Want(failures, roster.Officers == 2 && roster.Cars == 1,
                "ROSTER: the day that filled the man must not fill the car.");
            Want(failures, roster.Replace(13, filled) == 1 && roster.Cars == 2,
                "ROSTER: the car comes back on its own day.");

            // a scene with no campaign behind it has no replacement date either
            var dateless = new PoliceRoster(1, "P2", 1, 1);
            var loss = dateless.Lose(PoliceLoss.Officer, 0, config);
            Want(failures, loss != null && loss.BackOnDay == 0,
                "ROSTER: a loss on day nought is not given a day.");
            Want(failures, dateless.Replace(5, filled) == 0,
                "ROSTER: a hole with no day must never be filled by arithmetic.");
        }

        static void AnEmptyPrecinctSaysSoOnThePlaque(List<string> failures)
        {
            var config = new PoliceRosterConfig();
            var roster = new PoliceRoster(0, "Precinct 1", 1, 1);
            Want(failures, roster.Plaque().Contains("1 car") && roster.Plaque().Contains("1 man"),
                "PLAQUE: a working precinct prints its strength.");

            roster.Lose(PoliceLoss.Car, 4, config);
            Want(failures, roster.Plaque().Contains("back day"),
                "PLAQUE: a precinct with a hole prints the day it is filled.");

            roster.Lose(PoliceLoss.Officer, 4, config);
            Want(failures, roster.Empty && roster.Plaque().Contains("NO LAW"),
                "PLAQUE: an empty precinct must say NO LAW rather than nothing at all.");
        }

        // ------------------------------------------------------------------- the watch

        static void TheWatchTurnsOnTheHour(List<string> failures)
        {
            var config = new PoliceRosterConfig { DayShiftHour = 7f, NightShiftHour = 19f };
            Want(failures, PoliceShifts.At(7f, config) == PoliceWatch.Day,
                "WATCH: the day watch is on at its own hour.");
            Want(failures, PoliceShifts.At(18.99f, config) == PoliceWatch.Day,
                "WATCH: the day watch runs up to the night's hour.");
            Want(failures, PoliceShifts.At(19f, config) == PoliceWatch.Night &&
                           PoliceShifts.At(3f, config) == PoliceWatch.Night,
                "WATCH: the night watch holds the small hours.");

            // a config that puts the day watch across midnight must still name an hour
            var odd = new PoliceRosterConfig { DayShiftHour = 22f, NightShiftHour = 6f };
            Want(failures, PoliceShifts.At(23f, odd) == PoliceWatch.Day &&
                           PoliceShifts.At(5f, odd) == PoliceWatch.Day &&
                           PoliceShifts.At(12f, odd) == PoliceWatch.Night,
                "WATCH: a watch that wraps midnight must still cover every hour.");
        }

        static void TheNightHasTheCarsAndTheDayTheFeet(List<string> failures)
        {
            var config = new PoliceRosterConfig();
            var roster = new PoliceRoster(0, "Precinct 1", 4, 8);

            var dayCars = PoliceShifts.CarsOnDuty(roster, PoliceWatch.Day, config);
            var nightCars = PoliceShifts.CarsOnDuty(roster, PoliceWatch.Night, config);
            var dayFeet = PoliceShifts.FootOnDuty(roster, PoliceWatch.Day, config);
            var nightFeet = PoliceShifts.FootOnDuty(roster, PoliceWatch.Night, config);

            Want(failures, nightCars > dayCars, "WATCH: the night must put more cars out.");
            Want(failures, dayFeet > nightFeet, "WATCH: the day must put more men on foot.");
            Want(failures, dayFeet % 2 == 0 && nightFeet % 2 == 0,
                "WATCH: beat officers walk in pairs, so an odd man on duty is a bug.");
        }

        static void NobodyIsOnDutyWhoIsNotOnTheRoster(List<string> failures)
        {
            var config = new PoliceRosterConfig();
            var roster = new PoliceRoster(0, "Precinct 1", 3, 4);
            for (var i = 0; i < 2; i++) roster.Lose(PoliceLoss.Officer, 3, config);
            roster.Lose(PoliceLoss.Car, 3, config);

            foreach (PoliceWatch watch in Enum.GetValues(typeof(PoliceWatch)))
            {
                Want(failures, PoliceShifts.CarsOnDuty(roster, watch, config) <= roster.Cars,
                    "WATCH: more cars on duty than the precinct owns (" + watch + ").");
                Want(failures, PoliceShifts.FootOnDuty(roster, watch, config) <= roster.Officers,
                    "WATCH: more men on duty than the precinct has (" + watch + ").");
            }

            var empty = new PoliceRoster(1, "P2", 0, 0);
            Want(failures, PoliceShifts.CarsOnDuty(empty, PoliceWatch.Night, config) == 0 &&
                           PoliceShifts.FootOnDuty(empty, PoliceWatch.Day, config) == 0,
                "WATCH: an empty precinct puts nobody out at all.");

            var lone = new PoliceRoster(2, "P3", 0, 1);
            Want(failures, PoliceShifts.FootOnDuty(lone, PoliceWatch.Day, config) == 0,
                "WATCH: a lone officer must not be dealt as half of a beat pair.");
        }

        // -------------------------------------------------------------- the sentence

        static void TheDeedDecidesTheSentence(List<string> failures)
        {
            var rng = new Random(7);
            for (var i = 0; i < 200; i++)
            {
                var affray = Sentencing.Days(Deed.Affray, rng, false);
                Want(failures, affray >= 6 && affray <= 10,
                    "SENTENCE: an affray is six to ten days, not " + affray + ".");
            }
            for (var i = 0; i < 200; i++)
            {
                var murder = Sentencing.Days(Deed.Murder, rng, false);
                Want(failures, murder >= 15 && murder <= 25,
                    "SENTENCE: a murder is longer than an affray, not " + murder + ".");
            }
            for (var i = 0; i < 200; i++)
            {
                var lean = Sentencing.Days(Deed.Extortion, rng, false);
                Want(failures, lean >= 8 && lean <= 14,
                    "SENTENCE: extortion is eight to fourteen days, not " + lean + ".");
            }
            Want(failures, Sentencing.IsLife(Sentencing.Days(Deed.CopKilling, rng, false)),
                "SENTENCE: killing a policeman is life.");
        }

        /// <summary>GAN-245's "duza za sve": every band moved up, not only the new one -
        /// so the low end of each is at or above what its HIGH end used to be.</summary>
        static void TheBandsAreLonger(List<string> failures)
        {
            Want(failures, Sentencing.BandLow(Deed.Affray) >= 5,
                "SENTENCE: the affray band must start at or above the old high end of 5.");
            Want(failures, Sentencing.BandLow(Deed.Murder) >= 10,
                "SENTENCE: the murder band must start at or above the old high end of 10.");
            Want(failures, Sentencing.BandLow(Deed.Extortion) > Sentencing.BandLow(Deed.Affray),
                "SENTENCE: leaning on a shopkeeper is not a lesser thing than an affray.");
            Want(failures, Sentencing.BandLow(Deed.WitnessTampering) ==
                           Sentencing.BandLow(Deed.Extortion),
                "SENTENCE: intimidating a witness is the same band as the extortion " +
                "it was meant to bury.");
            Want(failures, Sentencing.DaysToCourt == 1,
                "SENTENCE: a man is held one day and tried the next (ruling of " +
                "2026-09-04) - the court leg runs on the first day tick after the arrest.");
            foreach (Deed deed in Enum.GetValues(typeof(Deed)))
            {
                Want(failures, Sentencing.BandHigh(deed) >= Sentencing.BandLow(deed),
                    "SENTENCE: " + deed + " has a band that runs backwards.");
                Want(failures, !string.IsNullOrEmpty(Sentencing.ChargeFor(deed)),
                    "SENTENCE: " + deed + " has no charge wording.");
                Want(failures, Verdict.BaseFor(deed) > 0f,
                    "VERDICT: " + deed + " has no conviction base.");
            }
        }

        /// <summary>CNTR-001's first sweep. Adding a deed is deliberately a failing
        /// change until every one of the five quiet default tables has been named.</summary>
        static void EveryDeedHasAnExplicitContract(List<string> failures)
        {
            var expected = new Dictionary<Deed, (int Low, int High, int Bail, float Base,
                string Charge)>
            {
                [Deed.Affray] = (6, 10, 5_000, Verdict.AffrayBase,
                    "Affray - discharging firearms in the street"),
                [Deed.Murder] = (15, 25, 25_000, Verdict.MurderBase, "Murder"),
                [Deed.CopKilling] = (Sentencing.Life, Sentencing.Life, 0,
                    Verdict.CopKillingBase, "Murder of a police officer"),
                [Deed.Extortion] = (8, 14, 2_000, Verdict.ExtortionBase, "Extortion"),
                [Deed.WitnessTampering] = (8, 14, 2_000, Verdict.ExtortionBase,
                    "Intimidating a witness"),
                [Deed.AssaultOnOfficer] = (11, 14, 15_000,
                    Verdict.AssaultOnOfficerBase, "Assault on a police officer"),
                [Deed.Resisting] = (2, 4, 5_000, Verdict.ResistingBase,
                    "Resisting arrest"),
                [Deed.Battery] = (10, 16, 4_000, Verdict.BatteryBase,
                    "Assault and battery"),
                [Deed.Trafficking] = (15, 30, 50_000, Verdict.TraffickingBase,
                    "Trafficking in cocaine, 400 grams or more"),
            };

            var values = (Deed[])Enum.GetValues(typeof(Deed));
            Want(failures, values.Length == expected.Count,
                "CNTR-001: the Deed enum changed without updating its exhaustive contract.");
            for (var i = 0; i < values.Length; i++)
            {
                var deed = values[i];
                if (!expected.TryGetValue(deed, out var row))
                {
                    failures.Add("CNTR-001: " + deed + " fell through the deed tables.");
                    continue;
                }
                Want(failures,
                    Sentencing.BandLow(deed) == row.Low &&
                    Sentencing.BandHigh(deed) == row.High &&
                    Sentencing.Bail(deed) == row.Bail &&
                    Math.Abs(Verdict.BaseFor(deed) - row.Base) < 0.0001f &&
                    Sentencing.ChargeFor(deed) == row.Charge,
                    "CNTR-001: " + deed + " does not match its complete deed contract.");
            }
        }

        static void TheBatteryHasItsOwnTerms(List<string> failures)
        {
            Want(failures,
                Sentencing.BandLow(Deed.Battery) == 10 &&
                Sentencing.BandHigh(Deed.Battery) == 16 &&
                Sentencing.Bail(Deed.Battery) == 4_000 &&
                Math.Abs(Verdict.BaseFor(Deed.Battery) - 0.30f) < 0.0001f,
                "CNTR-001: Battery is not 10-16 days, $4,000 bail and base 0.30.");
            Want(failures,
                Sentencing.BandLow(Deed.Battery) > Sentencing.BandLow(Deed.Extortion) &&
                Sentencing.BandHigh(Deed.Battery) < Sentencing.BandHigh(Deed.Murder),
                "CNTR-001: Battery does not sit strictly above extortion and below murder.");
        }

        static void AFoldedCountKeepsItsDeedsWeight(List<string> failures)
        {
            Want(failures,
                Sentencing.ExtraCountDays(Deed.Murder) == 5 &&
                Sentencing.ExtraCountDays(Deed.Battery) == 3 &&
                Sentencing.ExtraCountDays(Deed.Extortion) == 2 &&
                Sentencing.ExtraCountDays(Deed.Resisting) == 1 &&
                Sentencing.ExtraCountDays(null) == Sentencing.UnknownCountDays,
                "CNTR-004: folded counts are not floor(BandLow / 3), with legacy fallback 3.");

            var pipe = new PrisonPipeline();
            var heard = pipe.OpenCase(Deed.Extortion, 0, 10, 11);
            var murder = pipe.OpenCase(Deed.Murder, 0, 9, 0);
            var battery = pipe.OpenCase(Deed.Battery, 0, 9, 0);
            heard.Counts.Add(murder.CaseId);
            heard.Counts.Add(battery.CaseId);
            heard.ExtraCharges.Add(Deed.Resisting);
            Want(failures, pipe.FoldedCountDays(heard) == 9,
                "CNTR-004: the pipeline flattened deed-typed counts instead of adding 5+3+1.");
        }

        static void TheBeatingIsMeasuredBeforeItFrightensHim(List<string> failures)
        {
            var config = TerritoryFearConfig.Default;
            var block = new TerritoryBlockId("block:counter");
            var business = new TerritoryBusinessId("biz:counter");
            var gang = new TerritoryGangId(0);

            var publicFear = new TerritoryFearLedger(config);
            publicFear.Record(new TerritoryFearEvent(
                gang, block, TerritoryFearCategory.Assault, 2.5f,
                TerritoryFearVisibility.Public, 10d, business));
            var after = publicFear.BusinessFear(block, business, gang, 10d);

            var seenFear = new TerritoryFearLedger(config);
            seenFear.Record(new TerritoryFearEvent(
                gang, block, TerritoryFearCategory.Assault, 2.5f,
                TerritoryFearVisibility.Seen, 10d, business));
            var seen = seenFear.BusinessFear(block, business, gang, 10d);

            Want(failures, after >= Verdict.TestifyFearCap && seen < Verdict.TestifyFearCap,
                "CNTR-003: severity 2.5 Public does not clear the testimony cap while Seen stays below it.");
            var beforeCall = ComplaintRoll.Chance(0.5f, 0f, false, false);
            var afterCall = ComplaintRoll.Chance(
                0.5f, ComplaintRoll.Standing(after, config.FearCap, 0f), false, false);
            Want(failures, beforeCall > afterCall,
                "CNTR-003: the post-beating standing did not suppress the telephone, so call-before-fear cannot be observed.");
        }

        static void TheOwnersDocketMakesTheBeatingTampering(List<string> failures)
        {
            var pipe = new PrisonPipeline();
            var file = pipe.OpenCase(Deed.Extortion, 0, 10, 0,
                "", "THE COUNTER");
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant,
                BusinessId = "biz:counter",
                Name = "Milo Varga",
            });

            Want(failures,
                RoadDemo.WitnessWatch.DeedForBeating(pipe, "biz:counter", 0) ==
                    Deed.WitnessTampering &&
                RoadDemo.WitnessWatch.DeedForBeating(pipe, "biz:counter", 7) ==
                    Deed.Battery &&
                RoadDemo.WitnessWatch.DeedForBeating(pipe, "biz:other", 0) ==
                    Deed.Battery,
                "CNTR-003: only this willing owner on this house's open docket makes the beating tampering.");
        }

        static void ABodyOpensOneUncollaredMurderFile(List<string> failures)
        {
            var pipe = new PrisonPipeline();
            var none = RoadDemo.PoliceDispatch.OpenCivilianDeathCase(
                pipe, default, 12, "biz:counter", "THE COUNTER");
            var file = RoadDemo.PoliceDispatch.OpenCivilianDeathCase(
                pipe, new TerritoryGangId(7), 12, "biz:counter", "THE COUNTER");
            Want(failures, none == null && pipe.Cases.Count == 1,
                "CNTR-004: an unattributed body opened a file, or one attributed body did not open exactly one.");
            Want(failures,
                file != null && file.Deed == Deed.Murder && file.GangId == 7 &&
                file.BusinessId == "biz:counter" && file.Defendants.Count == 0 &&
                file.CourtDay == 12 + Sentencing.DaysToCourt && file.BodyEvidence,
                "CNTR-004: the body did not open a defendant-less murder file at its door.");
        }

        static void PoliceFireMakesABodyUnattributable(List<string> failures)
        {
            var oneHouse = RoadDemo.TerritoryRuntime.MurderAttribution(
                new[] { 7, 7 });
            var crossfire = RoadDemo.TerritoryRuntime.MurderAttribution(
                new[] { 7, 4 });
            var policeAndGang = RoadDemo.TerritoryRuntime.MurderAttribution(
                new[] { 7, RoadDemo.StreetAlarm.PoliceFaction });
            var policeOnly = RoadDemo.TerritoryRuntime.MurderAttribution(
                new[] { RoadDemo.StreetAlarm.PoliceFaction });

            Want(failures, oneHouse.IsValid && oneHouse.Value == 7,
                "CNTR-AUDIT: repeated fire by one house was not attributed to it.");
            Want(failures, !crossfire.IsValid && !policeAndGang.IsValid &&
                           !policeOnly.IsValid,
                "CNTR-AUDIT: a body beside competing or police fire was charged to a gang.");
        }

        static void AnIndoorComplaintHasNoPavementWitnesses(List<string> failures)
        {
            var indoors = new RoadDemo.StreetAlarm.Complaint { Indoors = true };
            var outside = new RoadDemo.StreetAlarm.Complaint { Indoors = false };
            Want(failures,
                !RoadDemo.PoliceDispatch.ComplaintHasPavementWitnesses(indoors) &&
                RoadDemo.PoliceDispatch.ComplaintHasPavementWitnesses(outside),
                "CNTR-004: an indoor act still snapshots people through the shop wall.");
        }

        static void TheDeadOwnerLeavesEveryOpenCase(List<string> failures)
        {
            var pipe = new PrisonPipeline();
            var ours = pipe.OpenCase(Deed.Extortion, 0, 10, 0, "biz:counter");
            var theirs = pipe.OpenCase(Deed.Extortion, 7, 10, 0, "biz:counter");
            var alreadySilent = pipe.OpenCase(Deed.Extortion, 4, 10, 0, "biz:counter");
            var elsewhere = pipe.OpenCase(Deed.Extortion, 0, 10, 0, "biz:other");
            foreach (var file in new[] { ours, theirs, alreadySilent, elsewhere })
                file.Witnesses.Add(new Witness
                {
                    Kind = WitnessKind.Complainant,
                    BusinessId = file.BusinessId,
                    Name = file.BusinessId,
                });
            alreadySilent.Witnesses[0].Standing = WitnessStanding.Withdrawn;

            var killed = RoadDemo.WitnessWatch.OwnerKilled(pipe, "biz:counter");
            Want(failures,
                killed == 3 && !ours.AnyWilling() && !theirs.AnyWilling() &&
                alreadySilent.Witnesses[0].Standing == WitnessStanding.Dead &&
                elsewhere.AnyWilling(),
                "EMPT-002: the dead proprietor did not leave every matching open case, ours and a rival's.");
        }

        static void ADeadComplaintCannotBecomeACount(List<string> failures)
        {
            var pipe = new PrisonPipeline();
            var dead = pipe.OpenCase(Deed.Extortion, 0, 9, 0, "biz:counter");
            dead.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant,
                BusinessId = "biz:counter",
                Standing = WitnessStanding.Dead,
            });
            var arrest = pipe.OpenCase(Deed.Affray, 0, 10, 11);
            Want(failures,
                pipe.AttachOpenComplaints(arrest, 10) == 0 &&
                dead.Status == CaseStatus.Open && arrest.Counts.Count == 0,
                "EMPT-002: a complaint with nobody willing was folded into a later arrest.");
        }

        static void ABodyStillBecomesOneCountWithoutWitnesses(List<string> failures)
        {
            var pipe = new PrisonPipeline();
            var body = RoadDemo.PoliceDispatch.OpenCivilianDeathCase(
                pipe, new TerritoryGangId(7), 10, "biz:counter", "THE COUNTER");
            body.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Eyewitness,
                Standing = WitnessStanding.Dead,
            });
            var arrest = pipe.OpenCase(Deed.Affray, 7, 11, 12);

            Want(failures, !body.AnyWilling() && body.AnyEvidence(),
                "CNTR-AUDIT: a murder body disappeared with its last living witness.");
            Want(failures,
                pipe.AttachOpenComplaints(arrest, 11) == 1 &&
                body.Status == CaseStatus.Folded && arrest.Counts.Count == 1 &&
                pipe.FoldedCountDays(arrest) ==
                    Sentencing.ExtraCountDays(Deed.Murder),
                "CNTR-AUDIT: a witnessless body did not survive as exactly one murder count.");
        }

        static void UnansweredFilesExpireWithTheirMemory(List<string> failures)
        {
            const int opened = 10;
            var pipe = new PrisonPipeline();
            var body = RoadDemo.PoliceDispatch.OpenCivilianDeathCase(
                pipe, new TerritoryGangId(7), opened, "biz:counter", "THE COUNTER");
            var complaint = pipe.OpenCase(
                Deed.Extortion, 7, opened, 0, "biz:other", "THE OTHER SHOP");
            var open = new List<CourtCase>();

            pipe.DayTick(opened + PrisonPipeline.ComplaintMemoryDays, null);
            pipe.OpenCases(7, open, opened + PrisonPipeline.ComplaintMemoryDays);
            Want(failures,
                pipe.FindCase(body.CaseId) == body &&
                pipe.FindCase(complaint.CaseId) == complaint &&
                open.Contains(body) && open.Contains(complaint),
                "CNTR-AUDIT: an unanswered file expired inside its full memory window.");

            pipe.DayTick(opened + PrisonPipeline.ComplaintMemoryDays + 1, null);
            pipe.OpenCases(7, open, opened + PrisonPipeline.ComplaintMemoryDays + 1);
            Want(failures,
                pipe.FindCase(body.CaseId) == null &&
                pipe.FindCase(complaint.CaseId) == null &&
                open.Count == 0,
                "CNTR-AUDIT: expired defendant-less files stayed in the save/map index.");
        }

        static void AssaultIsWorseThanAffrayAndBetterThanMurder(List<string> failures)
        {
            Want(failures,
                Sentencing.BandHigh(Deed.AssaultOnOfficer) >
                Sentencing.BandHigh(Deed.Affray) &&
                Sentencing.BandHigh(Deed.AssaultOnOfficer) <
                Sentencing.BandLow(Deed.Murder),
                "SENTENCE: assault on an officer must sit between affray and murder.");
            Want(failures,
                Sentencing.Bail(Deed.AssaultOnOfficer) > Sentencing.Bail(Deed.Affray) &&
                Sentencing.Bail(Deed.Resisting) == Sentencing.Bail(Deed.Affray),
                "SENTENCE: assault needs higher bail; resisting uses affray bail.");
            Want(failures,
                Verdict.BaseFor(Deed.AssaultOnOfficer) > Verdict.BaseFor(Deed.Affray),
                "VERDICT: firing on the law must be stronger than an affray case.");
        }

        static void ASecondActNeverDowngradesTheCharge(List<string> failures)
        {
            Want(failures,
                Sentencing.PrimaryCharge(Deed.Murder, Deed.AssaultOnOfficer) == Deed.Murder,
                "CHARGE: firing at the officer must not downgrade an existing murder.");
            Want(failures,
                Sentencing.PrimaryCharge(Deed.Extortion, Deed.AssaultOnOfficer) ==
                Deed.AssaultOnOfficer,
                "CHARGE: the fresh assault must lead an equal-band complaint file.");
            Want(failures,
                Sentencing.PrimaryCharge(Deed.Affray, Deed.CopKilling) == Deed.CopKilling,
                "CHARGE: killing an officer must always become the primary deed.");
        }

        static void AHoodGetsLessAndAMarkedLieutenantMore(List<string> failures)
        {
            var plain = Sentencing.Days(Deed.Murder, new Random(11), false,
                Rank.Lieutenant, false, 0, 0);
            var hood = Sentencing.Days(Deed.Murder, new Random(11), false,
                Rank.Hood, false, 0, 0);
            var named = Sentencing.Days(Deed.Murder, new Random(11), false,
                Rank.Lieutenant, true, 0, 0);

            Want(failures, hood < plain,
                "SENTENCE: sitna riba - a hood goes down for less than the man who " +
                "sent him (" + hood + " vs " + plain + ").");
            Want(failures, named > plain,
                "SENTENCE: a lieutenant the judge has read about goes down for more (" +
                named + " vs " + plain + ").");
            var small = Sentencing.Days(Deed.Affray, new Random(11), false,
                Rank.Hood, false, 5, 0);
            Want(failures, small >= Sentencing.HoodFloorDays,
                "SENTENCE: no hood walks out under the floor, whatever the scale says.");
            Want(failures, Sentencing.IsLife(Sentencing.Days(Deed.CopKilling,
                    new Random(11), false, Rank.Hood, false, 5, 3)),
                "SENTENCE: nothing scales a life sentence.");
        }

        static void ALawyerCutsTheDaysButNotLife(List<string> failures)
        {
            var alone = Sentencing.Days(Deed.Murder, new Random(5), false,
                Rank.Lieutenant, false, 0, 0);
            var counselled = Sentencing.Days(Deed.Murder, new Random(5), false,
                Rank.Lieutenant, false, Lawyer.MaxSkill, 0);
            Want(failures, counselled <= alone,
                "LAWYER: counsel never lengthens a sentence.");
            Want(failures, counselled >= Sentencing.BandLow(Deed.Murder),
                "LAWYER: a lawyer argues the days down, he does not invent a lesser " +
                "crime - the band's own floor holds.");
            Want(failures, Sentencing.IsLife(Sentencing.Days(Deed.CopKilling,
                    new Random(5), false, Rank.Lieutenant, false, Lawyer.MaxSkill, 0)),
                "LAWYER: nobody argues a cop-killer out of life.");

            var counts = Sentencing.Days(Deed.Extortion, new Random(5), false,
                Rank.Lieutenant, false, 0, 2);
            var single = Sentencing.Days(Deed.Extortion, new Random(5), false,
                Rank.Lieutenant, false, 0, 0);
            Want(failures, counts == single + 2 * Sentencing.UnknownCountDays,
                "SENTENCE: each attached count is worth its own days.");
        }

        static void LifeIsASentinelAndNotAnOverflow(List<string> failures)
        {
            Want(failures, Sentencing.Life > 0 && Sentencing.Life < int.MaxValue,
                "SENTENCE: life must be an explicit day number, never int.MaxValue.");
            Want(failures, Sentencing.Life + Sentencing.EscapeSurcharge > 0,
                "SENTENCE: a surcharge on top of life must not overflow into a release.");
            Want(failures, Sentencing.IsLife(Sentencing.Life + Sentencing.EscapeSurcharge),
                "SENTENCE: life plus a surcharge is still life.");
        }

        static void AnEscapeCostsHimTheSurcharge(List<string> failures)
        {
            var clean = Sentencing.Days(Deed.Affray, new Random(3), false);
            var escaper = Sentencing.Days(Deed.Affray, new Random(3), true);
            Want(failures, escaper == clean + Sentencing.EscapeSurcharge,
                "SENTENCE: a man who has been out of custody once gets the surcharge.");
            Want(failures, Sentencing.IsLife(Sentencing.Days(Deed.CopKilling, new Random(3), true)),
                "SENTENCE: a cop-killer's life is not lengthened by arithmetic.");
        }

        static void RunningCostsTwoMoreDays(List<string> failures)
        {
            var quiet = Sentencing.Days(Deed.Affray, new Random(37), false,
                Rank.Lieutenant, false, 0, 0, DoorAnswer.Quiet);
            var ran = Sentencing.Days(Deed.Affray, new Random(37), false,
                Rank.Lieutenant, false, 0, 0, DoorAnswer.Run);
            Want(failures, ran == quiet + Sentencing.ResistSurcharge,
                "ANSWER: running must add exactly the resisting surcharge.");
        }

        static void ExtraChargesAddDays(List<string> failures)
        {
            var one = Sentencing.Days(Deed.Extortion, new Random(41), false,
                Rank.Lieutenant, false, 0, 0);
            var three = Sentencing.Days(Deed.Extortion, new Random(41), false,
                Rank.Lieutenant, false, 0, 2);
            Want(failures, three == one + Sentencing.UnknownCountDays * 2,
                "ANSWER: deed-typed extra charges must reach the sentence count.");
        }

        static void SprungRecordsAnEscapeWithoutABooking(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(), Kind = EquipmentKind.Pistol,
                OwnerId = man.Id, HolderId = man.Id, PinnedTo = man.Id,
            });
            Want(failures, pipe.Sprung(roster, man.Id, 10),
                "CUSTODY: first-leg springing must be recorded without a booking.");
            Want(failures, pipe.Find(man.Id) == null && pipe.EverEscaped(man.Id),
                "CUSTODY: a sprung man stays out of the pipe but the city remembers him.");
            Want(failures, man.Status == CharacterStatus.Active &&
                           man.WantedLevel == WantedLevels.FreedFromTransfer,
                "CUSTODY: a sprung man stays active and is wanted for escaped custody.");
            Want(failures, roster.Equipment.Count == 0,
                "CUSTODY: a confiscated gun must not return to the crew armory.");

            man.Rank = Rank.Lieutenant;
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 12,
                answer: DoorAnswer.Quiet, sprung: true);
            var trialDay = 17;
            var marked = Notability.Marked(man, trialDay);
            var clean = Sentencing.Days(Deed.Affray,
                new Random(Sentencing.StreamFor(pipe.RosterSeed, man.Id, trialDay)),
                false, man.Rank, marked, 0, 0, DoorAnswer.Quiet);
            var escaped = Sentencing.Days(Deed.Affray,
                new Random(Sentencing.StreamFor(pipe.RosterSeed, man.Id, trialDay)),
                true, man.Rank, marked, 0, 0, DoorAnswer.Quiet);
            pipe.Convicted(roster, prisoner, trialDay);
            Want(failures, prisoner != null &&
                           prisoner.SentenceDays == escaped && escaped > clean,
                "CUSTODY: the next judge must apply the remembered escape surcharge.");
        }

        static void TheAnswerSurvivesASave(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = pipe.OpenCase(Deed.AssaultOnOfficer, 0, 10, 15,
                "shop-4", "THE BARBER");
            file.ExtraCharges.Add(Deed.Resisting);
            var prisoner = pipe.Book(roster, man.Id, Deed.AssaultOnOfficer, 10,
                file, DoorAnswer.Fight, sprung: true);
            file.Verdicts.Add(new CaseVerdict
            {
                CharacterId = man.Id,
                Outcome = CaseOutcome.Convicted,
                Answer = DoorAnswer.Fight,
                Sprung = true,
                Day = 15,
                Days = 18,
                OutOnDay = 33,
            });

            var saved = new CampaignFile
            {
                version = CampaignFile.Version,
                prisoners = PrisonSnapshot.Prisoners(pipe),
                cases = PrisonSnapshot.Cases(pipe),
                nextCaseId = pipe.NextCaseId,
                prisonRosterSeed = pipe.RosterSeed,
            };
            var back = new PrisonPipeline();
            PrisonSnapshot.Restore(back, saved);
            var loaded = back.Find(man.Id);
            var loadedFile = back.FindCase(file.CaseId);
            var verdict = loadedFile?.VerdictFor(man.Id);
            Want(failures, prisoner != null && loaded != null &&
                           loaded.Answer == DoorAnswer.Fight && loaded.Sprung,
                "SAVE: the prisoner's answer and sprung flag must survive.");
            Want(failures, loadedFile != null && loadedFile.ExtraCharges.Count == 1 &&
                           loadedFile.ExtraCharges[0] == Deed.Resisting,
                "SAVE: deed-typed extra charges must survive.");
            Want(failures, verdict != null && verdict.Answer == DoorAnswer.Fight &&
                           verdict.Sprung,
                "SAVE: the verdict archive must retain the answer at the door.");
        }

        static void RebookingKeepsTheWorstAnswer(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            Want(failures, pipe.Sprung(roster, man.Id, 9),
                "ANSWER: the first-leg escape must be remembered before booking.");
            var first = pipe.OpenCase(Deed.AssaultOnOfficer, 0, 10, 15);
            var prisoner = pipe.Book(roster, man.Id, Deed.AssaultOnOfficer, 10,
                first, DoorAnswer.Fight);
            Want(failures, prisoner != null && prisoner.Sprung,
                "ANSWER: booking after an escape must retain the sprung flag.");
            Want(failures, pipe.PostBail(roster, prisoner,
                    PrisonPipeline.BailPrice(prisoner), 10),
                "ANSWER: the re-booking contract needs the man back on bail.");
            var second = pipe.OpenCase(Deed.Affray, 0, 11, 16);
            var back = pipe.Book(roster, man.Id, Deed.Affray, 11,
                second, DoorAnswer.Quiet);
            Want(failures, back == prisoner && back != null &&
                           back.Answer == DoorAnswer.Fight && back.Sprung,
                "ANSWER: a quiet re-arrest must not soften a fight or lose springing.");
        }

        // ------------------------------------------------------------------- the pipe

        static Roster BookedRoster(out Character man, out PrisonPipeline pipe)
        {
            var roster = new Roster { Seed = 1987 };
            man = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Sal", Surname = "Rizzo",
            };
            roster.Members.Add(man);
            pipe = new PrisonPipeline { RosterSeed = roster.Seed };
            return roster;
        }

        static void HeldMeansHeldUntilAJudgeSaysOtherwise(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 10);

            Want(failures, prisoner != null && man.Status == CharacterStatus.Jailed,
                "PIPE: a man taken in goes on the books as held.");
            Want(failures, man.BackOnDay == 0,
                "PIPE: a man waiting on a judge has NO release date - a date would free him.");
            Want(failures, RosterOps.Discharge(roster, 9999) == 0,
                "PIPE: the day tick must not discharge a man who has not been sentenced.");
            Want(failures, prisoner.CourtDay == 10 + Sentencing.DaysToCourt,
                "PIPE: his day in court is an absolute day.");
            Want(failures, pipe.Book(roster, man.Id, Deed.Affray, 10) == null,
                "PIPE: one arrest per man - he cannot be booked twice over.");
        }

        static void TheVerdictLandsWhenTheTransferArrives(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10);
            var wanted = new List<Prisoner>();

            var courtDay = 10 + Sentencing.DaysToCourt;
            pipe.DayTick(courtDay - 1, wanted);
            Want(failures, wanted.Count == 0, "PIPE: nobody rides before his court day.");
            pipe.DayTick(courtDay, wanted);
            Want(failures, wanted.Count == 1 && wanted[0] == prisoner,
                "PIPE: his court day puts him up for transfer.");

            pipe.Away(prisoner);
            pipe.Convicted(roster, prisoner, courtDay);
            Want(failures, prisoner.Stage == PrisonStage.Sentenced && prisoner.SentenceDays >= 6,
                "PIPE: the verdict lands when the transfer arrives.");
            Want(failures, man.BackOnDay == courtDay + prisoner.SentenceDays,
                "PIPE: sentencing gives him an absolute day to come back on.");
            Want(failures, man.RapSheet.Count >= 2 &&
                           man.RapSheet[man.RapSheet.Count - 1].Outcome.Contains("Convicted"),
                "PIPE: the verdict is written on his sheet.");
            Want(failures, RosterOps.Discharge(roster, man.BackOnDay) == 1 &&
                           man.Status == CharacterStatus.Active,
                "PIPE: he stands up on his day like any other laid-up man.");
        }

        static void AWreckedTransferIsAFreeManUnarmed(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(), Kind = EquipmentKind.Pistol,
                OwnerId = man.Id, HolderId = man.Id,
            });

            var file = pipe.OpenCase(Deed.Murder, 0, 10,
                10 + Sentencing.DaysToCourt, "road-wreck", "THE ROAD");
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10, file);
            var riding = new List<Prisoner>();
            var courtDay = 10 + Sentencing.DaysToCourt;
            pipe.DayTick(courtDay, riding);
            pipe.Away(prisoner);
            pipe.Freed(roster, prisoner, courtDay);

            Want(failures, man.Status == CharacterStatus.Active && man.BackOnDay == 0,
                "PIPE: a man out of the back of a wrecked transfer is on his feet.");
            Want(failures, man.WantedLevel == WantedLevels.FreedFromTransfer,
                "PIPE: a freed man is wanted at the second grade.");
            Want(failures, roster.Equipment[0].HolderId == RosterEquipment.Unheld,
                "PIPE: he comes out with nothing in his hands - gear reaches a man " +
                "through his lieutenant only.");
            Want(failures, roster.Equipment[0].OwnerId == man.Id ||
                           roster.Equipment[0].OwnerId != RosterEquipment.Unheld,
                "PIPE: the gun still belongs to the branch that bought it.");
            Want(failures, file.Status == CaseStatus.Open &&
                           file.ExtraCharges.Contains(Deed.Resisting),
                "PIPE: a transfer escape leaves the case open with resisting on it.");
            Want(failures, pipe.EverEscaped(man.Id), "PIPE: the city remembers an escape.");
            Want(failures, pipe.Find(man.Id) == null, "PIPE: a freed man leaves the pipe.");

            // and the next judge adds it on
            var again = pipe.Book(roster, man.Id, Deed.Affray, 20);
            var secondCourtDay = 20 + Sentencing.DaysToCourt;
            pipe.DayTick(secondCourtDay, riding);
            pipe.Away(again);
            pipe.Convicted(roster, again, secondCourtDay);
            var clean = Sentencing.Days(Deed.Affray,
                new Random(Sentencing.StreamFor(roster.Seed, man.Id, secondCourtDay)), true,
                man.Rank, Notability.Marked(man, secondCourtDay), 0, 0);
            Want(failures, again.SentenceDays == clean,
                "PIPE: the surcharge reaches the sentence through the pipeline.");
        }

        /// <summary>
        /// GAN-237, PIPE-002. TWO LEGS, TWO DAYS. The verdict lands at the court; the van
        /// out of town is a second scheduled drive on a day of its own, and until it
        /// arrives the state does not have him. Both stages are asserted, because a pipe
        /// that jumped straight from the verdict to "serving" would leave the second road
        /// with nothing on it to take.
        /// </summary>
        static void TheSecondLegRunsOnItsOwnDay(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10);
            var wanted = new List<Prisoner>();
            var courtDay = 10 + Sentencing.DaysToCourt;

            pipe.DayTick(courtDay, wanted);
            Want(failures, wanted.Count == 1 && prisoner.Leg == PrisonLeg.Court,
                "PIPE: the first leg is the one to court.");
            pipe.Away(prisoner);
            pipe.Convicted(roster, prisoner, courtDay);

            Want(failures, prisoner.Stage == PrisonStage.Sentenced,
                "PIPE: sentenced is not delivered - he waits at the court for the van.");
            Want(failures, prisoner.PrisonDay == courtDay + Sentencing.DaysToPrison,
                "PIPE: the van has an absolute day of its own.");

            pipe.DayTick(courtDay, wanted);
            Want(failures, wanted.Count == 0,
                "PIPE: nobody rides out of town before the van's day.");

            pipe.DayTick(prisoner.PrisonDay, wanted);
            Want(failures, wanted.Count == 1 && prisoner.Leg == PrisonLeg.Prison &&
                           prisoner.Stage == PrisonStage.ForTransfer,
                "PIPE: the van's day puts him up for the second transfer.");

            // no car that day: he goes back to the COURT, not to the cells, and he is
            // not sentenced a second time
            pipe.BackToTheCells(prisoner, prisoner.PrisonDay);
            Want(failures, prisoner.Stage == PrisonStage.Sentenced &&
                           prisoner.PrisonDay == courtDay + Sentencing.DaysToPrison + 1,
                "PIPE: a van with no car leaves a sentenced man sentenced, riding tomorrow.");

            pipe.DayTick(prisoner.PrisonDay, wanted);
            pipe.Away(prisoner);
            Want(failures, prisoner.Stage == PrisonStage.InTransit,
                "PIPE: and he rides the next day.");
            pipe.Delivered(prisoner);
            Want(failures, prisoner.Stage == PrisonStage.Serving,
                "PIPE: the state has him only once the van arrives.");
            Want(failures, man.Status == CharacterStatus.Jailed &&
                           man.BackOnDay == courtDay + prisoner.SentenceDays,
                "PIPE: delivery changes nothing on his sheet - the verdict set the date.");
        }

        /// <summary>
        /// GAN-237. THE SECOND ROAD IS A ROAD. Wreck the van and the man walks away
        /// exactly as he does off the first car: off the books, on his feet, wanted, and
        /// with the escape on his sheet - a sentence already passed is not a cage.
        /// </summary>
        static void TheVanCanBeTakenLikeTheFirstCar(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10);
            var wanted = new List<Prisoner>();
            var courtDay = 10 + Sentencing.DaysToCourt;

            pipe.DayTick(courtDay, wanted);
            pipe.Away(prisoner);
            pipe.Convicted(roster, prisoner, courtDay);
            pipe.DayTick(prisoner.PrisonDay, wanted);
            pipe.Away(prisoner);

            var freed = pipe.Freed(roster, prisoner, prisoner.PrisonDay);
            Want(failures, freed != null && prisoner.Stage == PrisonStage.Freed,
                "PIPE: the van can be taken like the car to the court.");
            Want(failures, man.Status == CharacterStatus.Active && man.BackOnDay == 0,
                "PIPE: a man out of the back of the van is off the books entirely.");
            Want(failures, man.WantedLevel == WantedLevels.FreedFromTransfer,
                "PIPE: and he is wanted for it.");
            Want(failures, pipe.EverEscaped(man.Id),
                "PIPE: the city remembers a man taken off the second leg too.");
        }

        /// <summary>
        /// GAN-237. THE CAR COLLECTS HIM FIRST. A transfer is dispatched to wherever the
        /// man is being held and only carries him from there, so a car wrecked on its way
        /// to fetch him kills its escort and frees nobody: he was never in it. The pipe
        /// refuses the release rather than trusting the caller to remember.
        /// </summary>
        static void NobodyWalksOutOfACarHeWasNeverIn(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10);
            var wanted = new List<Prisoner>();
            var courtDay = 10 + Sentencing.DaysToCourt;

            pipe.DayTick(courtDay, wanted);
            Want(failures, prisoner.Stage == PrisonStage.ForTransfer,
                "PIPE: a man due today is up for transfer, not yet on the road.");

            Want(failures, pipe.Freed(roster, prisoner, courtDay) == null,
                "PIPE: a car wrecked before it collected him frees nobody.");
            Want(failures, man.Status == CharacterStatus.Jailed && man.WantedLevel == 0,
                "PIPE: and he is still on the books, unwanted.");

            // he rides tomorrow, and THAT car can be taken
            pipe.BackToTheCells(prisoner, courtDay);
            pipe.DayTick(courtDay + 1, wanted);
            pipe.Away(prisoner);
            Want(failures, pipe.Freed(roster, prisoner, courtDay + 1) != null,
                "PIPE: once he is in the back, wrecking the car frees him.");
        }

        static void NoCarNoConvoyAndHeWaitsADay(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 10);
            var wanted = new List<Prisoner>();

            var courtDay = 10 + Sentencing.DaysToCourt;
            pipe.DayTick(courtDay, wanted);
            Want(failures, wanted.Count == 1, "PIPE: he is due on his court day.");

            // the precinct has no car: he goes back in the cells and rides tomorrow
            pipe.BackToTheCells(prisoner, courtDay);
            Want(failures, prisoner.Stage == PrisonStage.Held &&
                           prisoner.CourtDay == courtDay + 1,
                "PIPE: a transfer with no car waits a day rather than losing the man.");
            Want(failures, man.Status == CharacterStatus.Jailed && man.BackOnDay == 0,
                "PIPE: waiting is still being held.");

            pipe.DayTick(courtDay + 1, wanted);
            Want(failures, wanted.Count == 1, "PIPE: and he is due again the next day.");
        }

        // ---------------------------------------------------------------- wanted men

        static void HiddenDaysClearTheGradeAndSightingsResetThem(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Vitale" };
            WantedLevels.Mark(man, WantedLevels.Fled, 5);
            Want(failures, man.WantedLevel == 1 && man.Wanted,
                "WANTED: a man who ran an arrest is wanted at the first grade.");

            WantedLevels.WentToGround(man, 5);
            Want(failures, !WantedLevels.DayTick(man, 7),
                "WANTED: two hidden days do not clear a grade that wants three.");
            Want(failures, WantedLevels.DayTick(man, 8) && man.WantedLevel == 0,
                "WANTED: three clear days out of sight clear the first grade.");

            // and a sighting is a reset to nothing, not a day back
            var seen = new Character { Id = 2, Surname = "Rocco" };
            WantedLevels.Mark(seen, WantedLevels.Fled, 5);
            WantedLevels.WentToGround(seen, 5);
            WantedLevels.Seen(seen);
            WantedLevels.WentToGround(seen, 7);
            Want(failures, !WantedLevels.DayTick(seen, 9),
                "WANTED: a street sighting resets the hidden days to NOTHING.");
            Want(failures, WantedLevels.DayTick(seen, 10) && seen.WantedLevel == 0,
                "WANTED: and the count starts again from the day he went back in.");

            var freed = new Character { Id = 3, Surname = "Pesce" };
            WantedLevels.Mark(freed, WantedLevels.FreedFromTransfer, 1);
            WantedLevels.WentToGround(freed, 1);
            Want(failures, !WantedLevels.DayTick(freed, 6) && WantedLevels.DayTick(freed, 8),
                "WANTED: a man freed off a transfer wants a week of it.");

            var shooter = new Character { Id = 4, Surname = "Neri" };
            WantedLevels.Mark(shooter, WantedLevels.ShotAtOfficer, 1);
            WantedLevels.WentToGround(shooter, 1);
            Want(failures,
                !WantedLevels.DayTick(shooter, 6) && WantedLevels.DayTick(shooter, 8),
                "WANTED: shooting at an officer also wants a week out of sight.");
        }

        static void ACopKillerNeverComesClean(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Bruno" };
            WantedLevels.Mark(man, WantedLevels.CopKiller, 2);
            WantedLevels.WentToGround(man, 2);
            Want(failures, !WantedLevels.DayTick(man, 100000) && man.WantedLevel == 3,
                "WANTED: no amount of hiding buys off a cop-killer.");
            Want(failures, WantedLevels.DaysToCool(WantedLevels.CopKiller) == WantedLevels.Never,
                "WANTED: the cop-killer's cure must be impossible, not merely long.");
        }

        static void ShotAtOfficerCoolsInSevenDays(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Neri" };
            WantedLevels.Mark(man, WantedLevels.ShotAtOfficer, 1);
            WantedLevels.WentToGround(man, 1);
            Want(failures, !WantedLevels.DayTick(man, 7) &&
                           WantedLevels.DayTick(man, 8),
                "WANTED: a shot at an officer needs seven clear days.");
        }

        static void SeverityOrdersTheMarks(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Moretti" };
            WantedLevels.Mark(man, WantedLevels.ShotAtOfficer, 2);
            WantedLevels.Mark(man, WantedLevels.Fled, 3);
            WantedLevels.Mark(man, WantedLevels.FreedFromTransfer, 3);
            Want(failures, man.WantedLevel == WantedLevels.ShotAtOfficer,
                "WANTED: flight and escape must not soften a shot-at-officer mark.");
            WantedLevels.Mark(man, WantedLevels.CopKiller, 4);
            Want(failures, man.WantedLevel == WantedLevels.CopKiller &&
                           WantedLevels.Severity(WantedLevels.CopKiller) >
                           WantedLevels.Severity(WantedLevels.ShotAtOfficer),
                "WANTED: an officer death must upgrade the mark and remain the top grade.");
        }

        static void AMissIsNotACopKiller(List<string> failures)
        {
            Want(failures,
                WantedLevels.ShotOutcome(false) == WantedLevels.ShotAtOfficer &&
                WantedLevels.ShotOutcome(true) == WantedLevels.CopKiller,
                "WANTED: the outcome helper must reserve cop-killer for an officer death.");
        }

        static void AMarkIsNeverDowngraded(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Gallo" };
            WantedLevels.Mark(man, WantedLevels.CopKiller, 3);
            WantedLevels.Mark(man, WantedLevels.Fled, 4);
            WantedLevels.Mark(man, WantedLevels.ShotAtOfficer, 4);
            Want(failures, man.WantedLevel == WantedLevels.CopKiller,
                "WANTED: flight or a missed shot must not demote a cop-killer.");

            var shooter = new Character { Id = 3, Surname = "Moretti" };
            WantedLevels.Mark(shooter, WantedLevels.Fled, 3);
            WantedLevels.Mark(shooter, WantedLevels.ShotAtOfficer, 4);
            WantedLevels.Mark(shooter, WantedLevels.FreedFromTransfer, 5);
            Want(failures, shooter.WantedLevel == WantedLevels.ShotAtOfficer &&
                           WantedLevels.Severity(WantedLevels.CopKiller) >
                           WantedLevels.Severity(WantedLevels.ShotAtOfficer) &&
                           WantedLevels.Severity(WantedLevels.ShotAtOfficer) >
                           WantedLevels.Severity(WantedLevels.FreedFromTransfer),
                "WANTED: severity, not the serialized integer, must rank the marks.");

            var flag = new Character { Id = 2, Surname = "Conti" };
            flag.Wanted = true;
            Want(failures, flag.WantedLevel == 1,
                "WANTED: the old boolean still marks a man at the first grade.");
            flag.WantedLevel = 3;
            flag.Wanted = true;
            Want(failures, flag.WantedLevel == 3,
                "WANTED: setting the old boolean must never pull a grade down.");
            flag.Wanted = false;
            Want(failures, flag.WantedLevel == 0,
                "WANTED: clearing the old boolean clears the grade.");
        }

        static void OutOfTownDrawsNoWage(List<string> failures)
        {
            var man = new Character
            {
                Id = 1, Surname = "Lombardi", Rank = Rank.Hood, WageAsked = 40,
            };
            Want(failures, Outfit.Wages.WageFor(man) == 40,
                "AWAY: a man on the street draws his envelope.");
            Want(failures, !WantedLevels.CanSendAway(man),
                "AWAY: only the worst grade is worth a bus ticket.");

            WantedLevels.Mark(man, WantedLevels.CopKiller, 5);
            Want(failures, WantedLevels.CanSendAway(man) && WantedLevels.SendAway(man, 5),
                "AWAY: a cop-killer can be sent out of the city.");
            Want(failures, Outfit.Wages.WageFor(man) == 0,
                "AWAY: a man in another state does not draw this one's payroll.");
            Want(failures, man.BackOnDay == 5 + WantedLevels.OutOfTownDays,
                "AWAY: his return is an absolute day.");
            Want(failures, man.WantedLevel == WantedLevels.CopKiller,
                "AWAY: he comes back exactly as wanted as he left.");
            Want(failures, !WantedLevels.SendAway(man, 6),
                "AWAY: a man already gone cannot be sent again.");
        }

        // ----------------------------------------------------------------- the deputy

        static Roster CrewRoster(out Character lieutenant, out Character deputy,
            out Character plodder, out Crew crew)
        {
            var roster = new Roster { Seed = 1987 };
            lieutenant = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Vito", Surname = "Ricci",
                Rank = Rank.Lieutenant,
            };
            lieutenant.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MaxHalfSteps);
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MaxHalfSteps);
            roster.Members.Add(lieutenant);

            deputy = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Nino", Surname = "Alba",
            };
            deputy.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MaxHalfSteps - 2);
            roster.Members.Add(deputy);

            plodder = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Gus", Surname = "Moro",
            };
            plodder.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MinHalfSteps);
            roster.Members.Add(plodder);

            crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            crew.HoodIds.Add(deputy.Id);
            crew.HoodIds.Add(plodder.Id);
            roster.Crews.Add(crew);
            return roster;
        }

        static void ADeputyRunsTheCrewWhileTheLeaderIsInside(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out _, out var crew);

            Want(failures, Command.EffectiveLieutenant(roster, crew) == lieutenant,
                "DEPUTY: a lieutenant on his feet runs his own crew.");

            RosterOps.Jail(roster, lieutenant.Id, 0, "Held at the station", "Affray");
            Want(failures, Command.EffectiveLieutenant(roster, crew) == deputy,
                "DEPUTY: a jailed lieutenant's crew is run by its best man.");

            var underDeputy = Command.PresenceFactorFor(roster, deputy.Id);
            lieutenant.Status = CharacterStatus.Active;
            var underLeader = Command.PresenceFactorFor(roster, deputy.Id);
            Want(failures, underDeputy < underLeader,
                "DEPUTY: the same men hold less under a deputy than under their leader.");
            Want(failures, Command.EffectiveLieutenant(roster, crew) == lieutenant,
                "DEPUTY: the leader has his crew back the day he is released.");
        }

        static void TheLeaderKeepsHisBranchOnPaper(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out _, out _, out var crew);
            RosterOps.Jail(roster, lieutenant.Id, 0, "Held", "Affray");

            Want(failures, crew.LieutenantId == lieutenant.Id,
                "DEPUTY: a jailed man stays the leader on paper - the branch is his.");
            Want(failures, roster.CrewOf(lieutenant.Id) == crew,
                "DEPUTY: and the crew is still his crew.");

            // a branch with nobody left standing has no deputy, and says so
            var empty = new Roster();
            var alone = new Character
            {
                Id = empty.NextCharacterId(), Surname = "Sole", Rank = Rank.Lieutenant,
                Status = CharacterStatus.Jailed,
            };
            empty.Members.Add(alone);
            var bare = new Crew { Id = empty.NextCrewId(), LieutenantId = alone.Id };
            empty.Crews.Add(bare);
            Want(failures, Command.EffectiveLieutenant(empty, bare) == null,
                "DEPUTY: a branch of nobody has no acting commander - null is the honest answer.");
        }

        // ------------------------------------------------------------------- the hideout

        static readonly LivingCity.Territory.TerritoryBusinessId Flat =
            new LivingCity.Territory.TerritoryBusinessId("res:block-04:flat:3:row-02");

        static readonly LivingCity.Territory.TerritoryBusinessId OtherFlat =
            new LivingCity.Territory.TerritoryBusinessId("res:block-11:flat:1:corner-01");

        /// <summary>
        /// GAN-235. ONE ADDRESS. Naming a second hideout MOVES the designation rather
        /// than adding one - a player with three hideouts has none he can name - and
        /// giving it up leaves the family with none, which is the state the flee code
        /// falls back to the nearest door of ours from.
        /// </summary>
        static void TheHideoutIsOneAddressAndItMoves(List<string> failures)
        {
            LivingCity.Territory.TerritoryHideout.Reset();
            Want(failures, !LivingCity.Territory.TerritoryHideout.Any,
                "HIDEOUT: a family starts with no hideout named.");

            Want(failures, LivingCity.Territory.TerritoryHideout.Designate(Flat),
                "HIDEOUT: naming a flat must take.");
            Want(failures,
                LivingCity.Territory.TerritoryHideout.Is(Flat) &&
                LivingCity.Territory.TerritoryHideout.Where == Flat,
                "HIDEOUT: the named flat is the hideout.");

            // naming it twice changes nothing, and says so
            Want(failures, !LivingCity.Territory.TerritoryHideout.Designate(Flat),
                "HIDEOUT: naming the same door twice is not a change.");

            var version = LivingCity.Territory.TerritoryHideout.Version;
            Want(failures, LivingCity.Territory.TerritoryHideout.Designate(OtherFlat),
                "HIDEOUT: a second address must be allowed - it MOVES the hideout.");
            Want(failures,
                LivingCity.Territory.TerritoryHideout.Is(OtherFlat) &&
                !LivingCity.Territory.TerritoryHideout.Is(Flat),
                "HIDEOUT: naming a second address moves it rather than keeping both.");
            Want(failures, LivingCity.Territory.TerritoryHideout.Version != version,
                "HIDEOUT: a move must move the repaint key.");

            Want(failures, LivingCity.Territory.TerritoryHideout.Clear(),
                "HIDEOUT: giving it up must take.");
            Want(failures,
                !LivingCity.Territory.TerritoryHideout.Any &&
                !LivingCity.Territory.TerritoryHideout.Where.IsValid,
                "HIDEOUT: given up, the family has no hideout at all.");
            Want(failures, !LivingCity.Territory.TerritoryHideout.Clear(),
                "HIDEOUT: giving up nothing is not a change.");

            // an id that names no premises is not an address
            Want(failures,
                !LivingCity.Territory.TerritoryHideout.Designate(default),
                "HIDEOUT: an invalid id must not become the hideout.");
            LivingCity.Territory.TerritoryHideout.Reset();
        }

        /// <summary>
        /// GAN-235. IT GOES WITH THE PAPER. Sell the flat, lose it, have it taken - the
        /// designation goes with the deed the moment it changes hands, because a map that
        /// points at a building somebody else owns is worse than a map with nothing on it.
        /// </summary>
        static void TheHideoutGoesWithItsDeed(List<string> failures)
        {
            const int us = 0;
            const int them = 2;

            LivingCity.Territory.TerritoryHideout.Reset();
            LivingCity.Territory.TerritoryHideout.Designate(Flat);

            // somebody else's deed changing hands is not our business
            LivingCity.Territory.TerritoryHideout.DeedChanged(OtherFlat, them, us);
            Want(failures, LivingCity.Territory.TerritoryHideout.Is(Flat),
                "HIDEOUT: another door changing hands must not move ours.");

            // and ours being written to us again is not a loss either
            LivingCity.Territory.TerritoryHideout.DeedChanged(Flat, us, us);
            Want(failures, LivingCity.Territory.TerritoryHideout.Is(Flat),
                "HIDEOUT: the deed staying ours must not clear the designation.");

            LivingCity.Territory.TerritoryHideout.DeedChanged(Flat, them, us);
            Want(failures, !LivingCity.Territory.TerritoryHideout.Any,
                "HIDEOUT: the hideout is lost with its deed.");
            LivingCity.Territory.TerritoryHideout.Reset();
        }

        // ------------------------------------------------------------ the telephone

        static void AFrightenedOwnerDoesNotRing(List<string> failures)
        {
            // A well-connected man who has been frightened for a month keeps quiet:
            // his fear is the whole of the cap and it eats his connections outright.
            var terrified = ComplaintRoll.Chance(0.8f, 100f, 100f, false, false);
            Want(failures, terrified <= ComplaintRoll.Floor + 0.001f,
                "COMPLAINT: a terrified shopkeeper does not pick up the telephone (" +
                terrified + ").");

            var calm = ComplaintRoll.Chance(0.8f, 0f, 100f, false, false);
            Want(failures, calm > terrified,
                "COMPLAINT: fear is what silences him - it must subtract.");

            // Standing is the LARGER of fear and the street already paying: a block
            // that pays the family to the last door is as quiet as a block it terrorised.
            var paidUp = ComplaintRoll.Chance(0.8f,
                ComplaintRoll.Standing(0f, 100f, 100f), false, false);
            Want(failures, paidUp <= ComplaintRoll.Floor + 0.001f,
                "COMPLAINT: a street that pays to the last door does not ring (" +
                paidUp + ").");
            Want(failures,
                System.Math.Abs(ComplaintRoll.Standing(60f, 100f, 30f) - 0.6f) < 0.001f &&
                System.Math.Abs(ComplaintRoll.Standing(20f, 100f, 70f) - 0.7f) < 0.001f,
                "COMPLAINT: standing is the larger of fear and the paying share.");

            var timid = ComplaintRoll.Chance(0.5f, 20f, 100f, false, true);
            var plain = ComplaintRoll.Chance(0.5f, 20f, 100f, false, false);
            Want(failures, timid < plain,
                "COMPLAINT: a man who wants no trouble rings less than one who does not care.");

            var stream = ComplaintRoll.StreamFor(1987, "shop-4", 11, 3);
            Want(failures, ComplaintRoll.Rings(0.5f, stream) == ComplaintRoll.Rings(0.5f, stream),
                "COMPLAINT: one stream must answer the same twice.");
            Want(failures,
                ComplaintRoll.StreamFor(1987, "shop-4", 11, 3) !=
                ComplaintRoll.StreamFor(1987, "shop-4", 12, 3),
                "COMPLAINT: two days must not share a stream.");
        }

        static void AConnectedOwnerRings(List<string> failures)
        {
            var connected = ComplaintRoll.Chance(0.85f, 10f, 100f, true, false);
            var nobody = ComplaintRoll.Chance(0.1f, 10f, 100f, false, false);
            Want(failures, connected > nobody,
                "COMPLAINT: a cousin at the precinct is worth something (" + connected +
                " vs " + nobody + ").");
            Want(failures, connected >= 0.5f,
                "COMPLAINT: a connected owner who is not frightened rings more often " +
                "than not (" + connected + ").");
            Want(failures, connected <= ComplaintRoll.Ceiling &&
                           nobody >= ComplaintRoll.Floor,
                "COMPLAINT: nobody is ever a certainty in either direction.");

            // The cousin outlives the standing: on a street that mostly pays, a
            // connected owner still rings now and then where a plain one has gone quiet.
            var connectedLate = ComplaintRoll.Chance(0.85f, 0.8f, true, false);
            var plainLate = ComplaintRoll.Chance(0.85f, 0.8f, false, false);
            Want(failures, connectedLate >= plainLate + ComplaintRoll.ConnectedBonus - 0.001f,
                "COMPLAINT: a cousin at the precinct is worth the same on an established " +
                "house (" + connectedLate + " vs " + plainLate + ").");

            var rang = 0;
            for (var seed = 0; seed < 200; seed++)
                if (ComplaintRoll.Rings(connected,
                        ComplaintRoll.StreamFor(1987, "deli", seed, 1)))
                    rang++;
            Want(failures, rang > 100,
                "COMPLAINT: over two hundred mornings a connected owner rings most of " +
                "them (" + rang + "/200).");
        }

        /// <summary>The arc the user asked for on 2026-09-03: a family nobody has heard
        /// of gets the telephone picked up on it most of the time; a family the street
        /// answers to does not.</summary>
        static void AStrangerIsRungOnAndAnEstablishedHouseIsNot(List<string> failures)
        {
            // Nobody fears us, nobody pays us: over every kind of shopkeeper the street
            // rings on the stranger far more often than not.
            var fresh = 0f;
            var settled = 0f;
            var samples = 0;
            for (var c = 0; c <= 10; c++)
            {
                var connections = c / 10f;
                fresh += ComplaintRoll.Chance(connections, 0f, false, false);
                settled += ComplaintRoll.Chance(connections, 0.7f, false, false);
                samples++;
            }
            fresh /= samples;
            settled /= samples;
            Want(failures, fresh >= 0.7f,
                "COMPLAINT: a stranger is rung on most of the time (" + fresh + ").");
            Want(failures, settled <= 0.1f,
                "COMPLAINT: a house the street answers to is hardly rung on (" + settled + ").");
            Want(failures, ComplaintRoll.Chance(0.5f, 0f, false, true) >= 0.5f,
                "COMPLAINT: even a man who wants no trouble rings on a stranger more " +
                "often than not.");

            // Monotone: more standing, fewer calls, every step of the way.
            var last = 2f;
            for (var s = 0; s <= 10; s++)
            {
                var now = ComplaintRoll.Chance(0.5f, s / 10f, false, false);
                Want(failures, now <= last + 0.0001f,
                    "COMPLAINT: standing must only ever silence (" + s + "/10 -> " + now + ").");
                last = now;
            }

            // Two hundred mornings on a fresh street against a plain shopkeeper: the
            // precinct hears from him most of them.
            var rang = 0;
            var chance = ComplaintRoll.Chance(0.5f, 0f, false, false);
            for (var seed = 0; seed < 200; seed++)
                if (ComplaintRoll.Rings(chance, ComplaintRoll.StreamFor(1987, "grocer", seed, 0)))
                    rang++;
            Want(failures, rang >= 130,
                "COMPLAINT: a stranger leaning on a grocer gets rung on most mornings (" +
                rang + "/200).");
        }

        // ------------------------------------------------------------------ the trial

        // ------------------------------------------------------------- the law sheet

        static LawSheetRows Sheet(
            PrisonPipeline pipe, Roster roster, int today, int lawyerSkill = 0,
            System.Func<CourtCase, bool> talks = null)
        {
            var rows = new LawSheetRows();
            LawSheet.Collect(pipe, roster, 0, today, lawyerSkill, talks, rows);
            return rows;
        }

        /// <summary>GAN-302. The docket is OUR open cases, soonest first, with the
        /// complaints nobody was taken for at the bottom - and another family's business
        /// is not on our sheet at all.</summary>
        static void TheDocketListsEveryOpenCaseOfOurs(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out _, out _);
            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };
            const int today = 10;

            var late = pipe.OpenCase(Deed.Murder, 0, 8, 20, "shop-1", "THE YARD");
            late.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceSawIt, Seed = 2 });
            pipe.Book(roster, lieutenant.Id, Deed.Murder, 8, late);

            var soon = pipe.OpenCase(Deed.Extortion, 0, 9, 14, "shop-2", "THE BARBER");
            soon.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant, Name = "Aldo Bruni", Seed = 7,
            });
            pipe.Book(roster, deputy.Id, Deed.Extortion, 9, soon);

            var complaint = pipe.OpenCase(Deed.Extortion, 0, 9, 0, "shop-3", "THE GROCER");
            var rival = pipe.OpenCase(Deed.Extortion, 3, 9, 14, "shop-4", "THEIR DELI");
            var closed = pipe.OpenCase(Deed.Affray, 0, 2, 5, "shop-5", "THE LOT");
            closed.Status = CaseStatus.Tried;

            var rows = Sheet(pipe, roster, today);

            Want(failures, rows.Docket.Count == 3,
                "SHEET: three of ours are open and " + rows.Docket.Count + " are listed.");
            Want(failures, rows.Docket.Count == 3 &&
                           rows.Docket[0].File == soon && rows.Docket[1].File == late,
                "SHEET: the soonest court day is at the top.");
            Want(failures, rows.Docket.Count == 3 &&
                           rows.Docket[2].File == complaint &&
                           rows.Docket[2].NobodyTaken,
                "SHEET: a complaint nobody was taken for is last and says so.");
            for (var i = 0; i < rows.Docket.Count; i++)
                Want(failures, rows.Docket[i].File != rival && rows.Docket[i].File != closed,
                    "SHEET: another family's case, and a case already closed, are not " +
                    "on our docket.");

            Want(failures, rows.Docket.Count > 0 &&
                           rows.Docket[0].DaysToCourt == 4,
                "SHEET: the card says how many days are left.");
            Want(failures, rows.Docket.Count > 0 &&
                           rows.Docket[0].Defendants.Count == 1 &&
                           rows.Docket[0].Defendants[0].Name == deputy.FullName,
                "SHEET: with the men who answer for it named.");
            Want(failures, rows.Inside.Count == 2,
                "SHEET: and both men are in the cells column.");
            Want(failures, rows.Counsel.Has == false,
                "SHEET: with no lawyer on the books the counsel box says so.");
        }

        /// <summary>GAN-302. The complainant's nerve is the PIPELINE'S gate, not a fear
        /// number the sheet compares for itself: a Connected owner turns up whatever the
        /// street has done to him, and the read must be taken on the witnesses the court
        /// will actually hear.</summary>
        static void TheReadIsTakenOnTheWitnessesTheCourtWillHear(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            pipe.Book(roster, man.Id, Deed.Extortion, 10, file);

            var counsel = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Vito", Surname = "Maranzano",
                Specialty = Specialty.Lawyer,
            };
            roster.Members.Add(counsel);
            var skill = Lawyer.Skill(counsel);

            var talking = Sheet(pipe, roster, 10, skill, _ => true).Docket[0];
            var silent = Sheet(pipe, roster, 10, skill, _ => false).Docket[0];

            Want(failures, talking.Witnesses[0].Standing == "will testify",
                "SHEET: a shopkeeper who is still talking says so.");
            Want(failures, silent.Witnesses[0].Standing == "may not testify — frightened",
                "SHEET: and one the gate says has been frightened off says THAT - on " +
                "the gate's word, never on a fear number read here.");
            Want(failures, silent.Witnesses[0].CanLeanOn,
                "SHEET: he is still a man the crew can reach.");
            Want(failures, !talking.Witnesses[1].CanLeanOn,
                "SHEET: a policeman is not leaned on.");

            // THE READ IS THE COURT'S ARITHMETIC, not the raw list's. Asserted against
            // the chance itself rather than against "the words changed": two adjacent
            // strengths can share a band, and a contract that only watched the words
            // would pass on a sheet that ignored the gate entirely.
            var withHim = Verdict.Leaning(Verdict.ConvictionChance(
                file.Deed, 0, false, true, true, 0, skill));
            var withoutHim = Verdict.Leaning(Verdict.ConvictionChance(
                file.Deed, 0, false, true, false, 0, skill));
            Want(failures, talking.Read == withHim,
                "SHEET: with the shopkeeper talking the read counts him (" +
                talking.Read + " vs " + withHim + ").");
            Want(failures, silent.Read == withoutHim,
                "SHEET: and with him frightened off the read is taken WITHOUT him (" +
                silent.Read + " vs " + withoutHim + ").");
            Want(failures, Verdict.ConvictionChance(
                     file.Deed, 0, false, true, true, 0, skill) >
                 Verdict.ConvictionChance(
                     file.Deed, 0, false, true, false, 0, skill),
                "SHEET: losing the complainant is a weaker case, whatever band it " +
                "lands in.");
            Want(failures, talking.Read != Verdict.NoCounselToAsk,
                "SHEET: a lawyer on the books reads it.");

            // And with nobody on the retainer there is nobody to ask at all.
            counsel.Specialty = Specialty.None;
            Want(failures, Sheet(pipe, roster, 10, 0, _ => true).Docket[0].Read ==
                           Verdict.NoCounselToAsk,
                "SHEET: with no lawyer on the books there is nobody to ask.");
            counsel.Specialty = Specialty.Lawyer;

            // Every witness gone is not a band at all - it is a certainty, and the
            // pipeline throws the case out before it rolls anything.
            for (var i = 0; i < file.Witnesses.Count; i++)
                file.Witnesses[i].Standing = WitnessStanding.Withdrawn;
            var nobody = Sheet(pipe, roster, 10, skill, _ => true).Docket[0];
            Want(failures, nobody.Read == Verdict.NoWitnessesLeft,
                "SHEET: with nobody left to give evidence the read is a certainty (" +
                nobody.Read + ").");
            counsel.Specialty = Specialty.None;
            Want(failures, Sheet(pipe, roster, 10, 0, _ => true).Docket[0].Read ==
                           Verdict.NoWitnessesLeft,
                "SHEET: and that one needs no lawyer to read - it is a fact, not an " +
                "opinion.");
        }

        /// <summary>GAN-302. One word table per thing. The sheet and the man's file read
        /// the SAME state through the same function, so a stage cannot be "on the road"
        /// on one page and "HELD" on the next.</summary>
        static void TheSheetAndTheFileUseOneWord(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);

            Want(failures, LivingCity.UI.LedgerText.StageLabel(PrisonStage.Held) ==
                           "in the cells",
                "WORDS: a man waiting on a judge is in the cells.");
            Want(failures, LivingCity.UI.LedgerText.StageLabel(PrisonStage.InTransit) ==
                           LivingCity.UI.LedgerText.StageLabel(PrisonStage.ForTransfer),
                "WORDS: both halves of a transfer read the same.");
            Want(failures, LivingCity.UI.LedgerText.StageBand(PrisonStage.Bailed) ==
                           "ON BAIL",
                "WORDS: the band head is the same word in capitals.");

            var rows = Sheet(pipe, roster, 10);
            Want(failures, rows.Inside.Count == 1 &&
                           rows.Inside[0].Stage ==
                           LivingCity.UI.LedgerText.StageLabel(prisoner.Stage),
                "WORDS: the cells column says exactly what the file's band says.");
            Want(failures, rows.Docket[0].Defendants[0].Stage ==
                           LivingCity.UI.LedgerText.StageLabel(prisoner.Stage),
                "WORDS: and so does the docket card.");

            pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10);
            pipe.SkipBail(prisoner);
            pipe.TryOnPaper(roster, file.CourtDay);
            var after = Sheet(pipe, roster, file.CourtDay);
            Want(failures, after.Docket.Count == 1 &&
                           after.Docket[0].Defendants[0].Stage == LawSheet.Hiding,
                "WORDS: a defendant the pipe no longer holds is hiding, not held.");
            Want(failures, after.Wanted.Count == 1 &&
                           after.Wanted[0].Word ==
                           WantedLevels.Word(man.WantedLevel),
                "WORDS: and the wanted column reads the level's own word.");
            Want(failures, RosterOps.CanCutLoose(man) == false,
                "WORDS: a man who has skipped is not in anybody's hands to sell.");
        }

        /// <summary>GAN-302. The archive is every closed case of ours, newest first,
        /// with a line per man - and a folded case says what it was rather than pretending
        /// to be a trial.</summary>
        static void TheArchiveReadsNewestFirst(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out _, out _);
            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };

            // One thrown out on day 12.
            var thrown = pipe.OpenCase(Deed.Extortion, 0, 8, 12, "shop-1", "THE BARBER");
            thrown.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant, Seed = 1,
                Standing = WitnessStanding.Withdrawn,
            });
            var walker = pipe.Book(roster, deputy.Id, Deed.Extortion, 8, thrown);
            pipe.Away(walker);
            pipe.Tried(roster, walker, 12);

            // One sold off on day 20.
            var sold = pipe.OpenCase(Deed.Extortion, 0, 15, 20, "shop-2", "THE GROCER");
            sold.Witnesses.Add(new Witness { Kind = WitnessKind.Complainant, Seed = 2 });
            pipe.Book(roster, lieutenant.Id, Deed.Extortion, 15, sold);
            pipe.CutLoose(lieutenant.Id, 20);

            var rows = Sheet(pipe, roster, 25);
            Want(failures, rows.Archive.Count == 2,
                "ARCHIVE: both closed cases are on it (" + rows.Archive.Count + ").");
            Want(failures, rows.Archive.Count == 2 && rows.Archive[0].File == sold,
                "ARCHIVE: newest first.");
            Want(failures, rows.Archive.Count == 2 &&
                           rows.Archive[0].Lines.Count == 1 &&
                           rows.Archive[0].Lines[0].Contains(lieutenant.FullName) &&
                           rows.Archive[0].Lines[0].Contains("cut loose"),
                "ARCHIVE: with a line naming the man and what became of him.");
            Want(failures, rows.Archive.Count == 2 &&
                           rows.Archive[1].Lines.Count == 1 &&
                           rows.Archive[1].Lines[0].Contains("dismissed"),
                "ARCHIVE: a case thrown out reads as a dismissal.");
            Want(failures, rows.Docket.Count == 0,
                "ARCHIVE: and nothing closed is still on the docket.");
        }

        // -------------------------------------------------------- the case's own book

        /// <summary>GAN-302. Every way a man comes off a case leaves a line ON THE CASE,
        /// so the ledger's archive prints what happened instead of reassembling it from
        /// the prose on his rap sheet.</summary>
        static void EveryCloseWritesAVerdict(List<string> failures)
        {
            // Convicted: the police saw it, so there is no walking away from this one.
            var roster = BookedRoster(out var man, out var pipe);
            // Day 18 is a morning this seed's stream convicts on (0.85 against him and
            // the draw falls under it). The contract is the RECORD, not the roll: the
            // assertions below tie the line to the sentence the prisoner actually got,
            // so a stream that changed its mind would fail rather than pass quietly.
            const int courtDay = 18;
            var heavy = pipe.OpenCase(Deed.Murder, 0, 10, courtDay, "shop-1", "THE BARBER");
            heavy.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceSawIt, Seed = 3 });
            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10, heavy);
            pipe.Away(prisoner);
            pipe.Tried(roster, prisoner, courtDay);
            var convicted = heavy.VerdictFor(man.Id);
            Want(failures, prisoner.Stage == PrisonStage.Sentenced,
                "VERDICT: the police saw this one and he goes down.");
            Want(failures, convicted != null && convicted.Outcome == CaseOutcome.Convicted,
                "VERDICT: a conviction is written on the case.");
            Want(failures, convicted != null && convicted.Days == prisoner.SentenceDays &&
                           convicted.OutOnDay == prisoner.OutOnDay &&
                           convicted.Day == courtDay,
                "VERDICT: with the days, the day he comes out and the day it was decided.");

            // Dismissed: every witness silenced before the morning.
            var second = BookedRoster(out var quiet, out var quietPipe);
            var thrownOut = WordAgainstWord(quietPipe, 10);
            for (var i = 0; i < thrownOut.Witnesses.Count; i++)
                thrownOut.Witnesses[i].Standing = WitnessStanding.Withdrawn;
            var walker = quietPipe.Book(second, quiet.Id, Deed.Extortion, 10, thrownOut);
            quietPipe.Away(walker);
            quietPipe.Tried(second, walker, thrownOut.CourtDay);
            var dismissed = thrownOut.VerdictFor(quiet.Id);
            Want(failures, dismissed != null && dismissed.Outcome == CaseOutcome.Dismissed,
                "VERDICT: a case thrown out for want of a witness says so on the case.");
            Want(failures, thrownOut.Status == CaseStatus.Dismissed,
                "VERDICT: and the case itself is a dismissal.");

            // Cut loose: the boss closed his file while he was inside.
            var third = BookedRoster(out var sold, out var soldPipe);
            var file = soldPipe.OpenCase(Deed.Extortion, 0, 10, 15, "shop-7", "THE GROCER");
            file.Witnesses.Add(new Witness { Kind = WitnessKind.Complainant, Seed = 1 });
            soldPipe.Book(third, sold.Id, Deed.Extortion, 10, file);
            soldPipe.CutLoose(sold.Id, 12);
            var cut = file.VerdictFor(sold.Id);
            Want(failures, cut != null && cut.Outcome == CaseOutcome.CutLoose &&
                           cut.Day == 12,
                "VERDICT: a man cut loose leaves the case with a line saying so.");

            // And nobody is written down twice.
            soldPipe.CutLoose(sold.Id, 13);
            Want(failures, file.Verdicts.Count == 1,
                "VERDICT: one line per man per case, whatever the caller does twice.");
        }

        /// <summary>GAN-302. A case whose counts were folded into a later one, and a case
        /// every man was taken off before a judge saw him, are CLOSED but are not
        /// trials - the archive must not print them as verdicts that happened.</summary>
        static void AFoldedCaseIsNotATrial(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out _, out _);
            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };

            var file = pipe.OpenCase(Deed.Extortion, 0, 10, 15, "shop-4", "THE DELICATESSEN");
            file.Witnesses.Add(new Witness { Kind = WitnessKind.Complainant, Seed = 1 });
            pipe.Book(roster, lieutenant.Id, Deed.Extortion, 10, file);
            pipe.Book(roster, deputy.Id, Deed.Extortion, 10, file);

            pipe.CutLoose(deputy.Id, 11);
            pipe.CutLoose(lieutenant.Id, 11);
            Want(failures, file.Status == CaseStatus.Folded,
                "FOLDED: a case every man was sold off is closed WITHOUT a trial (" +
                file.Status + ").");
            Want(failures, !file.AnyTried,
                "FOLDED: and nothing on it was ever heard.");

            // One man tried and the rest dropped is still a TRIAL: what the court did
            // to him is the case's own history.
            var second = CrewRoster(out var boss, out var mate, out _, out _);
            var pipeTwo = new PrisonPipeline { RosterSeed = second.Seed };
            var heard = pipeTwo.OpenCase(Deed.Murder, 0, 10, 15, "shop-5", "THE YARD");
            heard.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceSawIt, Seed = 4 });
            var first = pipeTwo.Book(second, boss.Id, Deed.Murder, 10, heard);
            pipeTwo.Book(second, mate.Id, Deed.Murder, 10, heard);
            pipeTwo.Away(first);
            pipeTwo.Tried(second, first, 15);
            pipeTwo.CutLoose(mate.Id, 15);
            Want(failures, heard.Status == CaseStatus.Tried,
                "FOLDED: a case one man was heard on is a trial, whatever became of " +
                "the men after him.");
        }

        /// <summary>GAN-302. A man who skips his bail stays a defendant, but the case
        /// cannot sit on the docket for the rest of the campaign drawing witness markers
        /// for a trial that will never be listed.</summary>
        static void ASkippedManLapsesOffTheDocket(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);
            pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10);
            pipe.SkipBail(prisoner);
            pipe.TryOnPaper(roster, file.CourtDay);

            Want(failures, file.Status == CaseStatus.Open,
                "LAPSE: the morning he skips, the case is still open against him.");

            var open = new List<CourtCase>();
            pipe.DayTick(file.CourtDay + PrisonPipeline.ComplaintMemoryDays, null);
            pipe.OpenCases(0, open);
            Want(failures, open.Contains(file),
                "LAPSE: and it stays open through the whole memory window - a re-arrest " +
                "inside it folds the old charge in as a count.");

            pipe.DayTick(file.CourtDay + PrisonPipeline.ComplaintMemoryDays + 1, null);
            Want(failures, file.Status == CaseStatus.Folded,
                "LAPSE: past it, a case with nobody left to try lapses (" +
                file.Status + ").");
            Want(failures, file.VerdictFor(man.Id) != null &&
                           file.VerdictFor(man.Id).Outcome == CaseOutcome.BailForfeit,
                "LAPSE: carrying the forfeit it collected, so the archive says what he did.");

            // A complaint nobody was ever taken for can become a count only during
            // the same memory window; after that it is neither tried nor archived.
            var complaint = pipe.OpenCase(Deed.Extortion, 0, 10, 0, "shop-9");
            pipe.DayTick(500, null);
            Want(failures, pipe.FindCase(complaint.CaseId) == null,
                "LAPSE: a complaint past its count window stayed in the live/save docket.");
        }

        static CourtCase WordAgainstWord(PrisonPipeline pipe, int day)
        {
            var file = pipe.OpenCase(Deed.Extortion, 0, day, day + Sentencing.DaysToCourt,
                "shop-4", "THE DELICATESSEN");
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant, Name = "Aldo Bruni", Seed = 7,
                BusinessId = "shop-4",
            });
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.PoliceFoundThem, Name = "Officer Doyle", Seed = 8,
            });
            return file;
        }

        static void WordAgainstWordMostlyWalks(List<string> failures)
        {
            var pipe = new PrisonPipeline { RosterSeed = 1987 };
            var file = WordAgainstWord(pipe, 10);

            var bare = Verdict.ConvictionChance(file, 0, 0);
            var counselled = Verdict.ConvictionChance(file, 0, Lawyer.MaxSkill);
            Want(failures, bare > 0.5f && bare < 0.6f,
                "TRIAL: his word against the shopkeeper's is a little over even with no " +
                "counsel (" + bare + ").");
            Want(failures, counselled <= 0.2f,
                "TRIAL: moja rec protiv njegove - a good lawyer walks him (" +
                counselled + ").");

            var convictions = 0;
            for (var i = 0; i < 200; i++)
                if (Verdict.Convicts(counselled, Sentencing.StreamFor(1987, i, 10)))
                    convictions++;
            Want(failures, convictions <= 40,
                "TRIAL: over two hundred rolls a lawyered word-against-word is lost by " +
                "the prosecution four times in five (" + convictions + "/200).");
        }

        static void TwoEyewitnessesConvict(List<string> failures)
        {
            var pipe = new PrisonPipeline { RosterSeed = 1987 };
            var file = WordAgainstWord(pipe, 10);
            for (var i = 0; i < 2; i++)
                file.Witnesses.Add(new Witness
                {
                    Kind = WitnessKind.Eyewitness, Name = "Passer-by " + i, Seed = 20 + i,
                });

            var chance = Verdict.ConvictionChance(file, 0, 0);
            Want(failures, chance >= 0.7f,
                "TRIAL: two people who saw it happen convict (" + chance + ").");

            var third = new Witness { Kind = WitnessKind.Eyewitness, Seed = 30 };
            file.Witnesses.Add(third);
            Want(failures, Verdict.ConvictionChance(file, 0, 0) == chance,
                "TRIAL: a third man who saw the same thing is not a third case.");

            var convictions = 0;
            for (var i = 0; i < 200; i++)
                if (Verdict.Convicts(chance, Sentencing.StreamFor(1987, i, 10)))
                    convictions++;
            Want(failures, convictions >= 120,
                "TRIAL: and the rolls agree (" + convictions + "/200).");
        }

        static void NoWitnessesIsADismissal(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);

            Want(failures, prisoner != null && file.Defendants.Contains(man.Id),
                "CASE: an arrest puts the man on the docket.");
            Want(failures, prisoner.CourtDay == 10 + Sentencing.DaysToCourt,
                "CASE: and his court day is the case's.");

            foreach (var witness in file.Witnesses)
                witness.Standing = WitnessStanding.Withdrawn;
            Want(failures, !file.AnyWilling(), "CASE: nobody is left to give evidence.");

            pipe.Away(prisoner);
            pipe.Tried(roster, prisoner, 15);

            Want(failures, file.Status == CaseStatus.Dismissed,
                "TRIAL: a case with no witness of any kind is thrown out before the roll.");
            Want(failures, man.Status == CharacterStatus.Active && man.BackOnDay == 0,
                "TRIAL: and the men walk.");
            Want(failures, man.WantedLevel == 0,
                "TRIAL: a man the court let go is not a man on the run.");
            Want(failures, man.RapSheet.Count >= 2 &&
                           man.RapSheet[man.RapSheet.Count - 1].Outcome ==
                           Sentencing.DismissedOutcome,
                "TRIAL: the dismissal is written on his sheet.");
            Want(failures, pipe.Find(man.Id) == null, "TRIAL: and he leaves the pipe.");
        }

        static void RecognitionRecoversTheOpenCase(List<string> failures)
        {
            for (var repetition = 0; repetition < 5; repetition++)
            {
                var roster = BookedRoster(out var man, out var pipe);
                var original = WordAgainstWord(pipe, 10 + repetition);
                original.GangId = roster.GangId;
                original.Defendants.Add(man.Id);
                var closed = pipe.OpenCase(Deed.Battery, roster.GangId, 1, 2);
                closed.Status = CaseStatus.Tried;
                var recovered = pipe.CaseForArrest(man.Id, roster.GangId, Deed.Resisting,
                    12 + repetition, closed, out var opened);
                Want(failures, recovered == original && !opened && original.AnyEvidence(),
                    "RECOGNITION: a rebuilt crew must recover the man's open docket.");
                original.Status = CaseStatus.Tried;
                var fresh = pipe.CaseForArrest(man.Id, roster.GangId, Deed.CopKilling,
                    12 + repetition, closed, out opened);
                Want(failures, opened && fresh != original && fresh != closed &&
                    fresh.Status == CaseStatus.Open && fresh.Deed == Deed.CopKilling &&
                    fresh.CourtDay == 13 + repetition,
                    "RECOGNITION: a closed file must not replace a new recognition charge.");
                Want(failures, pipe.CaseForArrest(man.Id, roster.GangId, Deed.Resisting,
                    12 + repetition, fresh, out opened) == fresh && !opened,
                    "RECOGNITION: an open remembered file must not be duplicated.");
                var foreign = pipe.OpenCase(Deed.Extortion, roster.GangId + 1, 10, 11);
                var own = pipe.CaseForArrest(man.Id + 1, roster.GangId,
                    WantedLevels.Charge(WantedLevels.ShotAtOfficer), 12, foreign, out opened);
                Want(failures, opened && own.GangId == roster.GangId &&
                    own.Deed == Deed.AssaultOnOfficer && foreign.Defendants.Count == 0,
                    "RECOGNITION: another family's file cannot become this man's charge.");
            }
        }

        static void RecognitionAfterForfeitGetsANewHearing(List<string> failures)
        {
            for (var repetition = 0; repetition < 5; repetition++)
            {
                var roster = BookedRoster(out var man, out var pipe);
                var old = WordAgainstWord(pipe, 10);
                old.GangId = roster.GangId;
                var prisoner = pipe.Book(roster, man.Id, old.Deed, 10, old);
                pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10);
                pipe.SkipBail(prisoner);
                pipe.TryOnPaper(roster, prisoner.CourtDay);
                var recognised = pipe.CaseForArrest(man.Id, roster.GangId, Deed.Resisting,
                    13, old, out var opened);
                Want(failures, opened && recognised != old && old.HasDefendant(man.Id),
                    "RECOGNITION: spotting a bail skipper must not erase his old charge.");
                var otherDefendant = man.Id + 100;
                old.Defendants.Add(otherDefendant);
                var recaptured = pipe.Book(roster, man.Id, recognised.Deed, 13, recognised);
                Want(failures, recaptured.CourtDay == 14 && recognised.Counts.Contains(old.CaseId) &&
                    !old.HasDefendant(man.Id) && old.HasDefendant(otherDefendant) &&
                    old.Status == CaseStatus.Open && man.WantedLevel == 0,
                    "RECOGNITION: recapture carries the old count to a new date without losing other defendants.");
                recognised.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceFoundThem });
                pipe.Away(recaptured);
                pipe.Tried(roster, recaptured, 14);
                Want(failures, old.VerdictFor(man.Id).Outcome == CaseOutcome.BailForfeit &&
                    recognised.VerdictFor(man.Id) != null &&
                    recognised.VerdictFor(man.Id).Outcome != CaseOutcome.BailForfeit,
                    "RECOGNITION: the forfeit and the later trial must both remain readable.");
                var third = pipe.CaseForArrest(man.Id, roster.GangId, Deed.Resisting,
                    15, recognised, out opened);
                Want(failures, opened && third != recognised,
                    "RECOGNITION: an already judged man cannot reuse that verdict's case.");
            }
        }

        static void BookingEndsThePursuitButKeepsTheCase(List<string> failures)
        {
            for (var repetition = 0; repetition < 5; repetition++)
            {
                foreach (var level in new[] { WantedLevels.Fled, WantedLevels.FreedFromTransfer,
                    WantedLevels.ShotAtOfficer, WantedLevels.CopKiller })
                {
                    var roster = BookedRoster(out var man, out var pipe);
                    WantedLevels.Mark(man, level, 8);
                    WantedLevels.WentToGround(man, 9);
                    var file = pipe.CaseForArrest(man.Id, roster.GangId,
                        WantedLevels.Charge(level), 10, null, out _);
                    file.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceFoundThem });
                    var prisoner = pipe.Book(roster, man.Id, file.Deed, 10, file);
                    Want(failures, prisoner != null && man.WantedLevel == 0 && man.HidingSince == 0 &&
                        file.Status == CaseStatus.Open && file.HasDefendant(man.Id) &&
                        prisoner.Deed == WantedLevels.Charge(level),
                        "CUSTODY: capture ends the pursuit while retaining its actual charge.");
                    WantedLevels.Mark(man, level, 11);
                    Want(failures, pipe.Book(roster, man.Id, file.Deed, 11, file) == null &&
                        man.WantedLevel == level,
                        "CUSTODY: a refused duplicate booking must not clear a fresh mark.");
                }
                var bailedRoster = BookedRoster(out var bailedMan, out var bailedPipe);
                WantedLevels.Mark(bailedMan, WantedLevels.Fled, 9);
                var bailFile = WordAgainstWord(bailedPipe, 10);
                var bailed = bailedPipe.Book(bailedRoster, bailedMan.Id, bailFile.Deed, 10, bailFile);
                bailedPipe.PostBail(bailedRoster, bailed, PrisonPipeline.BailPrice(bailed), 10);
                bailedPipe.TryOnPaper(bailedRoster, bailed.CourtDay);
                Want(failures, bailed.Stage != PrisonStage.Skipped && bailedMan.WantedLevel == 0,
                    "BAIL: yesterday's captured flight cannot forfeit today's attended hearing.");
            }
        }

        static void LegacyCourtOutcomesEndThePursuit(List<string> failures)
        {
            for (var repetition = 0; repetition < 5; repetition++)
            {
                foreach (var level in new[] { WantedLevels.Fled, WantedLevels.FreedFromTransfer,
                    WantedLevels.ShotAtOfficer, WantedLevels.CopKiller })
                {
                    var roster = BookedRoster(out var man, out var pipe);
                    var file = pipe.OpenCase(WantedLevels.Charge(level), roster.GangId, 10, 11);
                    var prisoner = pipe.Book(roster, man.Id, file.Deed, 10, file);
                    // A legacy held save retained the pursuit from before booking.
                    WantedLevels.Mark(man, level, 9);
                    pipe.Away(prisoner);
                    pipe.Tried(roster, prisoner, 11);
                    Want(failures, man.WantedLevel == 0 &&
                        (prisoner.Stage == PrisonStage.Sentenced ||
                         man.Status == CharacterStatus.Active && pipe.Find(man.Id) == null) &&
                        file.VerdictFor(man.Id) != null,
                        "TRIAL: an adjudicated legacy prisoner must not retain his old pursuit.");
                    if (!file.BodyEvidence)
                        Want(failures, file.Status == CaseStatus.Dismissed,
                            "TRIAL: a case with no remaining evidence must be dismissed.");
                }
                var convictedRoster = BookedRoster(out var convicted, out var convictedPipe);
                var serving = convictedPipe.Book(convictedRoster, convicted.Id, Deed.Affray, 10);
                WantedLevels.Mark(convicted, WantedLevels.Fled, 9);
                convictedPipe.Away(serving);
                convictedPipe.Tried(convictedRoster, serving, 11);
                Want(failures, serving.Stage == PrisonStage.Sentenced && convicted.WantedLevel == 0,
                    "TRIAL: a convicted man serves his charge without a second pursuit for it.");
            }
        }

        static void ThePoliceWhoSawItAreNotSilenced(List<string> failures)
        {
            var pipe = new PrisonPipeline { RosterSeed = 1987 };
            var file = pipe.OpenCase(Deed.Murder, 0, 10, 15);
            file.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceSawIt, Seed = 4 });
            file.Witnesses.Add(new Witness { Kind = WitnessKind.Eyewitness, Seed = 5 });

            var full = Verdict.ConvictionChance(file, 0, 0);
            file.Witnesses[1].Standing = WitnessStanding.Dead;
            var afterwards = Verdict.ConvictionChance(file, 0, 0);

            Want(failures, afterwards < full,
                "TRIAL: killing the passer-by takes his evidence off the case.");
            Want(failures, file.AnyWilling(),
                "TRIAL: the policeman who watched it is still there.");
            Want(failures, afterwards >= Verdict.MurderBase + Verdict.PoliceSawItWeight - 0.001f,
                "TRIAL: and there is no cure for one of those (" + afterwards + ").");
            Want(failures, !file.Witnesses[0].CanBePressured,
                "TRIAL: a policeman is not leaned on and not shot at over this.");
        }

        static void AWithdrawnWitnessIsOffTheCase(List<string> failures)
        {
            var pipe = new PrisonPipeline { RosterSeed = 1987 };
            var file = WordAgainstWord(pipe, 10);
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Eyewitness, Name = "Ruth Pardo", Seed = 41,
            });

            var before = file.WillingEyewitnesses();
            file.Witnesses[2].Standing = WitnessStanding.Withdrawn;
            Want(failures, file.WillingEyewitnesses() == before - 1,
                "WITNESS: a man who has been leaned on is off the count.");
            Want(failures, file.WillingCount() == 2,
                "WITNESS: and off the case's own tally.");
            Want(failures, Verdict.ConvictionChance(file, 0, 0) < 0.6f,
                "WITNESS: which is what leaning on him was for.");

            // A steady man is harder to move than a nervous one, and the roll is the
            // same twice off one stream.
            var steady = 0;
            var nervous = 0;
            for (var seed = 0; seed < 400; seed++)
            {
                if (WitnessPressure.Withdraws(9_000_017,
                        Sentencing.StreamFor(1987, seed, 3))) steady++;
                if (WitnessPressure.Withdraws(5,
                        Sentencing.StreamFor(1987, seed, 3))) nervous++;
            }
            Want(failures,
                WitnessPressure.Nerve(9_000_017) > WitnessPressure.Nerve(5)
                    ? steady < nervous : nervous < steady,
                "WITNESS: nerve is what decides whether a lean silences him.");
        }

        static void AnOpenComplaintIsAnExtraCount(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            const int today = 30;
            var walkedAway = pipe.OpenCase(Deed.Extortion, 0, today - 2, 0,
                "shop-1", "THE BARBER");
            walkedAway.Witnesses.Add(new Witness { Kind = WitnessKind.Complainant, Seed = 1 });
            var stale = pipe.OpenCase(Deed.Extortion, 0,
                today - PrisonPipeline.ComplaintMemoryDays - 1, 0, "shop-2");
            var somebodyElse = pipe.OpenCase(Deed.Extortion, 3, today - 2, 0, "shop-3");

            Want(failures, pipe.OpenComplaintsAgainst(0, today) == 1,
                "DOCKET: only a complaint inside the memory window is still on it.");

            var file = WordAgainstWord(pipe, today);
            var attached = pipe.AttachOpenComplaints(file, today);
            Want(failures, attached == 1 && file.Counts.Count == 1,
                "DOCKET: an open complaint against the same crew is an extra count.");
            Want(failures, walkedAway.Status == CaseStatus.Folded,
                "DOCKET: and a count folded in cannot be charged a second time - " +
                "FOLDED, not tried: nobody stood up for it.");
            Want(failures, stale.Status == CaseStatus.Open &&
                           somebodyElse.Status == CaseStatus.Open,
                "DOCKET: nothing folds in a stale complaint or another family's.");
            Want(failures, pipe.AttachOpenComplaints(file, today) == 0,
                "DOCKET: and folding twice attaches nothing.");

            var heard = today + Sentencing.DaysToCourt;
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, today, file);
            pipe.Away(prisoner);
            pipe.Tried(roster, prisoner, heard);
            if (prisoner.Stage == PrisonStage.Sentenced)
            {
                var without = Sentencing.Days(Deed.Extortion,
                    new Random(Sentencing.StreamFor(roster.Seed, man.Id, heard)), false,
                    man.Rank, Notability.Marked(man, heard), 0, 0);
                Want(failures, prisoner.SentenceDays > without,
                    "DOCKET: the count reaches the sentence through the pipeline.");
            }
        }

        // ------------------------------------------------------------------- the bail

        static void BailComesBackAsAMan(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);

            Want(failures, pipe.BailRefusal(prisoner, 0) == LivingCity.UI.LedgerText.ReasonNoCounsel,
                "BAIL: with no lawyer on the books nobody gets a hearing listed.");
            Want(failures, pipe.BailRefusal(prisoner, Lawyer.BailSkill) == null,
                "BAIL: with counsel he can be bailed.");
            Want(failures, PrisonPipeline.BailPrice(prisoner) == Sentencing.Bail(Deed.Extortion) &&
                           PrisonPipeline.BailPrice(prisoner) > 0,
                "BAIL: the price comes off the deed.");

            Want(failures, pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10),
                "BAIL: the money is up and he walks out.");
            Want(failures, man.Status == CharacterStatus.Active &&
                           man.BailedUntil == prisoner.CourtDay,
                "BAIL: he is an ordinary man on the street until his day.");
            Want(failures, prisoner.Stage == PrisonStage.Bailed && pipe.Find(man.Id) != null,
                "BAIL: and still on the docket.");
            pipe.Discharged(roster);
            Want(failures, pipe.Find(man.Id) != null,
                "BAIL: a bailed man is not swept out of the pipe by the day tick.");

            // his day comes and he turns up: tried on paper with the rest of the case
            pipe.TryOnPaper(roster, prisoner.CourtDay);
            Want(failures, man.BailedUntil == 0,
                "BAIL: the day came and the bail is spent either way.");
            Want(failures, man.Status == CharacterStatus.Jailed ||
                           man.Status == CharacterStatus.Active,
                "BAIL: he was tried on paper - convicted or walking, never held on.");
            Want(failures, man.WantedLevel == 0,
                "BAIL: a man who turned up is not wanted for anything.");
        }

        static void SkippedBailIsWTwoAndTheMoneyIsGone(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);
            var price = PrisonPipeline.BailPrice(prisoner);
            pipe.PostBail(roster, prisoner, price, 10);

            Want(failures, pipe.SkipBail(prisoner) && prisoner.SkipOrdered,
                "BAIL: the boss can tell him not to turn up.");

            var forfeited = new List<Prisoner>();
            pipe.TryOnPaper(roster, prisoner.CourtDay, forfeited);

            Want(failures, forfeited.Count == 1 && prisoner.Stage == PrisonStage.Skipped,
                "BAIL: a man who skips forfeits it.");
            Want(failures, man.WantedLevel == WantedLevels.FreedFromTransfer,
                "BAIL: and the city looks for him on transfer terms - a week out of sight.");
            Want(failures, prisoner.BailPaid == price && man.BailPaid == price,
                "BAIL: what it cost is kept, because a forfeit is not refunded.");
            Want(failures, file.Status == CaseStatus.Open && file.HasDefendant(man.Id),
                "BAIL: the case stays open against him.");
            var forfeitLine = file.VerdictFor(man.Id);
            Want(failures, forfeitLine != null &&
                           forfeitLine.Outcome == CaseOutcome.BailForfeit,
                "BAIL: and the case's own record says he forfeited it, so the archive " +
                "can print it whether or not the case is ever heard.");
            Want(failures, man.RapSheet[man.RapSheet.Count - 1].Outcome ==
                           Sentencing.BailForfeitOutcome,
                "BAIL: and it is written on his sheet.");
            Want(failures, man.BailedUntil == 0, "BAIL: the day is spent.");
        }

        // ---------------------------------------------------------------- the sale

        static void CutLooseCostsTheCrewMost(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out var plodder,
                out var crew);
            var other = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Carlo", Surname = "Sesto",
                Rank = Rank.Lieutenant,
            };
            roster.Members.Add(other);
            roster.Crews.Add(new Crew { Id = roster.NextCrewId(), LieutenantId = other.Id });
            var stranger = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Enzo", Surname = "Prato",
            };
            roster.Members.Add(stranger);
            roster.Organization.BossHoodIds.Add(stranger.Id);

            Want(failures, !RosterOps.CutLoose(roster, lieutenant.Id).Ok,
                "CUT LOOSE: nobody is sold off a street corner - he has to be inside.");

            RosterOps.Jail(roster, lieutenant.Id, 0, "Held at the station", "Extortion");
            var before = new[] { deputy.Loyalty, plodder.Loyalty, other.Loyalty, stranger.Loyalty };

            var changes = new List<PersonalityChange>();
            Want(failures, RosterOps.CutLoose(roster, lieutenant.Id, changes).Ok,
                "CUT LOOSE: a man the city is holding can be cut loose.");
            Want(failures, lieutenant.Status == CharacterStatus.CutLoose && lieutenant.Gone,
                "CUT LOOSE: he is off the books for good.");

            var crewLoss = before[0] - deputy.Loyalty;
            var lieutenantLoss = before[2] - other.Loyalty;
            var outfitLoss = before[3] - stranger.Loyalty;
            Want(failures, crewLoss > lieutenantLoss && lieutenantLoss > outfitLoss &&
                           outfitLoss > 0,
                "CUT LOOSE: his own men take it hardest, then the other lieutenants, " +
                "then the rest (" + crewLoss + " / " + lieutenantLoss + " / " +
                outfitLoss + ").");
            Want(failures, crewLoss <= Loyalty.CutLooseCrewHit,
                "CUT LOOSE: no man takes more than the outfit-wide figure.");
            Want(failures, changes.Count >= 3,
                "CUT LOOSE: every movement is printed with a reason - nothing is hidden.");
            foreach (var change in changes)
                Want(failures, !string.IsNullOrEmpty(change.Reason),
                    "CUT LOOSE: a loyalty move with no reason on it is a hidden percentage.");

            Want(failures, LivingCity.Outfit.Wages.WageFor(lieutenant) == 0,
                "CUT LOOSE: and he draws nothing, because he is gone.");

            // a hood is a smaller loss and is felt as one
            var second = CrewRoster(out _, out var hood, out var mate, out _);
            RosterOps.Jail(second, hood.Id, 0, "Held", "Affray");
            var mateBefore = mate.Loyalty;
            RosterOps.CutLoose(second, hood.Id);
            Want(failures, mateBefore - mate.Loyalty < crewLoss,
                "CUT LOOSE: selling a hood is a smaller thing than selling a lieutenant.");
        }

        /// <summary>Codex review, 2026-09-02: a bailed man is an ordinary man on the
        /// street and can walk into another arrest. The pipe used to book nothing at
        /// all for him, which left the crew stood in the road with its hands up and the
        /// new case with no defendant on it.</summary>
        static void ARearrestPutsABailedManBack(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var first = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, first);
            pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10);

            var second = pipe.OpenCase(Deed.Murder, 0, 12, 12 + Sentencing.DaysToCourt);
            second.Witnesses.Add(new Witness { Kind = WitnessKind.PoliceSawIt, Seed = 3 });

            var again = pipe.Book(roster, man.Id, Deed.Murder, 12, second);
            Want(failures, again == prisoner,
                "REARREST: a bailed man taken again goes back in the pipe he never left.");
            Want(failures, again != null && again.Stage == PrisonStage.Held,
                "REARREST: and he is HELD, not still out on the street.");
            Want(failures, man.Status == CharacterStatus.Jailed && man.BailedUntil == 0,
                "REARREST: the books say he is inside and his bail day is spent.");
            Want(failures, second.HasDefendant(man.Id),
                "REARREST: the new case has him on it.");
            Want(failures, again != null && again.CourtDay == second.CourtDay,
                "REARREST: and he is heard on the new case's own day.");
            Want(failures, again != null && again.Deed == Deed.Murder,
                "REARREST: he is held on the graver of the two deeds - read off the " +
                "sentence bands, so appending a deed cannot re-rank the ones above it.");
            Want(failures, second.Counts.Contains(first.CaseId) &&
                           first.Status != CaseStatus.Open,
                "REARREST: what he was already answering for is folded in as a count " +
                "rather than left open for a trial nobody will list.");
            Want(failures, again != null && again.BailPaid > 0,
                "REARREST: the bail money is not handed back.");
        }

        /// <summary>Codex review, 2026-09-02: cutting the only defendant loose used to
        /// leave a case nothing could ever resolve - still drawing witness markers and
        /// taking leans for a trial that cannot happen.</summary>
        static void CuttingLooseTheLastDefendantClosesTheCase(List<string> failures)
        {
            var roster = CrewRoster(out var lieutenant, out var deputy, out _, out _);
            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };
            var file = pipe.OpenCase(Deed.Extortion, 0, 10, 15, "shop-4", "THE DELICATESSEN");
            file.Witnesses.Add(new Witness { Kind = WitnessKind.Complainant, Seed = 1 });
            pipe.Book(roster, lieutenant.Id, Deed.Extortion, 10, file);
            pipe.Book(roster, deputy.Id, Deed.Extortion, 10, file);

            pipe.CutLoose(deputy.Id);
            Want(failures, !file.HasDefendant(deputy.Id),
                "CUT LOOSE: a man the outfit dropped is off the case.");
            Want(failures, file.Status == CaseStatus.Open,
                "CUT LOOSE: the case goes on for the men still on it.");

            pipe.CutLoose(lieutenant.Id);
            Want(failures, file.Status != CaseStatus.Open,
                "CUT LOOSE: a case with nobody left on it is closed, not left open " +
                "forever with its witness markers on the map.");
            Want(failures, pipe.Find(lieutenant.Id) == null,
                "CUT LOOSE: and he is out of the pipe.");

            // A complaint nobody was ever taken for is the OTHER case with no
            // defendants, and it must stay open - that is what becomes a count later.
            var complaint = pipe.OpenCase(Deed.Extortion, 0, 10, 0, "shop-9");
            pipe.CutLoose(lieutenant.Id);
            Want(failures, complaint.Status == CaseStatus.Open,
                "CUT LOOSE: a complaint nobody answered for is not closed by somebody " +
                "else's sale.");
        }

        /// <summary>Codex review, 2026-09-02: a bailed man tried on paper changed from
        /// active to serving a sentence with nothing said about it anywhere, because
        /// only the forfeits were reported back.</summary>
        static void EveryManTriedOnPaperIsReported(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var file = WordAgainstWord(pipe, 10);
            var prisoner = pipe.Book(roster, man.Id, Deed.Extortion, 10, file);
            pipe.PostBail(roster, prisoner, PrisonPipeline.BailPrice(prisoner), 10);

            var forfeited = new List<Prisoner>();
            var tried = new List<Prisoner>();
            var done = pipe.TryOnPaper(roster, prisoner.CourtDay, forfeited, tried);

            Want(failures, done == 1, "PAPER: his day came and he was dealt with.");
            Want(failures, forfeited.Count == 0,
                "PAPER: a man who turned up forfeits nothing.");
            Want(failures, tried.Count == 1 && tried[0] == prisoner,
                "PAPER: and the caller is told he was tried, so the verdict can be " +
                "printed like any other.");
            Want(failures, tried.Count == 1 &&
                           (tried[0].Stage == PrisonStage.Sentenced ||
                            tried[0].Stage == PrisonStage.Cleared),
                "PAPER: a paper trial ends in a verdict, not in limbo.");
        }

        /// <summary>GAN-324: the shared city pipeline must return every defendant to
        /// the house he belonged to when he was booked. Character ids are unique, but
        /// keeping the house explicitly also prevents a rival's paper trial from being
        /// attempted against the player's roster.</summary>
        static void ARivalPrisonerKeepsHisHouseThroughTheVerdict(List<string> failures)
        {
            var rival = Roster.Create(7);
            rival.Seed = 1987;
            var man = new Character
            {
                Id = rival.NextCharacterId(), FirstName = "Rocco", Surname = "Serra",
            };
            rival.Members.Add(man);

            var pipe = new PrisonPipeline { RosterSeed = rival.Seed };
            var prisoner = pipe.Book(rival, man.Id, Deed.Affray, 10);
            var bailed = prisoner != null && pipe.PostBail(rival, prisoner,
                PrisonPipeline.BailPrice(prisoner), 10);
            var player = Roster.Create(0);
            var playerMan = new Character
            {
                Id = player.NextCharacterId(), FirstName = "Paul", Surname = "Vale",
            };
            player.Members.Add(playerMan);

            Want(failures, bailed && prisoner.GangId == rival.GangId,
                "PRESS/POLICE: a rival prisoner must keep his house id at booking.");
            Want(failures, pipe.TryOnPaper(player, prisoner.CourtDay) == 0 &&
                           playerMan.Status == CharacterStatus.Active &&
                           pipe.TryOnPaper(rival, prisoner.CourtDay) == 1 &&
                           (man.Status == CharacterStatus.Jailed ||
                            man.Status == CharacterStatus.Active),
                "PRESS/POLICE: the verdict must run against the prisoner's own roster.");
        }

        static void StandingByHimPaysAPointAWeek(List<string> failures)
        {
            var week = Loyalty.DriftEveryDays;
            var changes = new List<PersonalityChange>();

            var carried = new Character { Id = 1, Surname = "Tosi", Loyalty = 50 };
            Loyalty.Drift(carried, true, false, 0, week, 1, changes, null,
                leaderInside: true, leaderPaid: true);
            Want(failures, carried.Loyalty == 50 + Loyalty.PaidOnTimeGain + Loyalty.StoodByGain,
                "LOYALTY: a crew whose leader is inside and still paid gains its point " +
                "(" + carried.Loyalty + ").");

            var sold = new Character { Id = 2, Surname = "Fava", Loyalty = 50 };
            Loyalty.Drift(sold, true, false, 0, week, 1, changes, null,
                leaderInside: true, leaderPaid: false);
            Want(failures, sold.Loyalty == 50 + Loyalty.PaidOnTimeGain - Loyalty.InsideUnpaidLoss,
                "LOYALTY: a leader inside on an empty envelope costs the crew instead " +
                "(" + sold.Loyalty + ").");

            var ordinary = new Character { Id = 3, Surname = "Neri", Loyalty = 50 };
            Loyalty.Drift(ordinary, true, false, 0, week, 1, changes, null);
            Want(failures, ordinary.Loyalty == 50 + Loyalty.PaidOnTimeGain,
                "LOYALTY: a crew with nobody inside is untouched by any of it.");

            var told = false;
            foreach (var change in changes)
                if (change.Reason == "the boss is standing by a man inside") told = true;
            Want(failures, told,
                "LOYALTY: standing by a man is printed like every other movement.");
        }
    }
}
