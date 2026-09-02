using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;

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
            ("TheLeaningIsReadableWithoutANumber", TheLeaningIsReadableWithoutANumber),
            ("ARosterNeverGoesAboveItsStrength", ARosterNeverGoesAboveItsStrength),
            ("AHoleIsFilledOnItsOwnDayAndNoSooner", AHoleIsFilledOnItsOwnDayAndNoSooner),
            ("AnEmptyPrecinctSaysSoOnThePlaque", AnEmptyPrecinctSaysSoOnThePlaque),
            ("TheWatchTurnsOnTheHour", TheWatchTurnsOnTheHour),
            ("TheNightHasTheCarsAndTheDayTheFeet", TheNightHasTheCarsAndTheDayTheFeet),
            ("NobodyIsOnDutyWhoIsNotOnTheRoster", NobodyIsOnDutyWhoIsNotOnTheRoster),
            ("TheDeedDecidesTheSentence", TheDeedDecidesTheSentence),
            ("LifeIsASentinelAndNotAnOverflow", LifeIsASentinelAndNotAnOverflow),
            ("AnEscapeCostsHimTheSurcharge", AnEscapeCostsHimTheSurcharge),
            ("HeldMeansHeldUntilAJudgeSaysOtherwise", HeldMeansHeldUntilAJudgeSaysOtherwise),
            ("TheVerdictLandsWhenTheTransferArrives", TheVerdictLandsWhenTheTransferArrives),
            ("AWreckedTransferIsAFreeManUnarmed", AWreckedTransferIsAFreeManUnarmed),
            ("NoCarNoConvoyAndHeWaitsADay", NoCarNoConvoyAndHeWaitsADay),
            ("HiddenDaysClearTheGradeAndSightingsResetThem", HiddenDaysClearTheGradeAndSightingsResetThem),
            ("ACopKillerNeverComesClean", ACopKillerNeverComesClean),
            ("AMarkIsNeverDowngraded", AMarkIsNeverDowngraded),
            ("OutOfTownDrawsNoWage", OutOfTownDrawsNoWage),
            ("ADeputyRunsTheCrewWhileTheLeaderIsInside", ADeputyRunsTheCrewWhileTheLeaderIsInside),
            ("TheLeaderKeepsHisBranchOnPaper", TheLeaderKeepsHisBranchOnPaper),

            // ------------------------------------------------ GAN-245: the complaint,
            // the trial, the lawyer, bail, the witnesses and the sale
            ("TheBandsAreLonger", TheBandsAreLonger),
            ("AHoodGetsLessAndAMarkedLieutenantMore", AHoodGetsLessAndAMarkedLieutenantMore),
            ("ALawyerCutsTheDaysButNotLife", ALawyerCutsTheDaysButNotLife),
            ("AFrightenedOwnerDoesNotRing", AFrightenedOwnerDoesNotRing),
            ("AConnectedOwnerRings", AConnectedOwnerRings),
            ("WordAgainstWordMostlyWalks", WordAgainstWordMostlyWalks),
            ("TwoEyewitnessesConvict", TwoEyewitnessesConvict),
            ("NoWitnessesIsADismissal", NoWitnessesIsADismissal),
            ("ThePoliceWhoSawItAreNotSilenced", ThePoliceWhoSawItAreNotSilenced),
            ("AWithdrawnWitnessIsOffTheCase", AWithdrawnWitnessIsOffTheCase),
            ("AnOpenComplaintIsAnExtraCount", AnOpenComplaintIsAnExtraCount),
            ("BailComesBackAsAMan", BailComesBackAsAMan),
            ("SkippedBailIsWTwoAndTheMoneyIsGone", SkippedBailIsWTwoAndTheMoneyIsGone),
            ("CutLooseCostsTheCrewMost", CutLooseCostsTheCrewMost),
            ("StandingByHimPaysAPointAWeek", StandingByHimPaysAPointAWeek),
            ("ARearrestPutsABailedManBack", ARearrestPutsABailedManBack),
            ("CuttingLooseTheLastDefendantClosesTheCase",
                CuttingLooseTheLastDefendantClosesTheCase),
            ("EveryManTriedOnPaperIsReported", EveryManTriedOnPaperIsReported),
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
            Want(failures, Sentencing.DaysToCourt >= 5,
                "SENTENCE: there must be days enough between the arrest and the court " +
                "day to play bail, a lawyer and the witnesses in.");
            for (var deed = Deed.Affray; deed <= Deed.WitnessTampering; deed++)
                Want(failures, Sentencing.BandHigh(deed) >= Sentencing.BandLow(deed),
                    "SENTENCE: " + deed + " has a band that runs backwards.");
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
            Want(failures, counts == single + 2 * Sentencing.ExtraCountDays,
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

            var prisoner = pipe.Book(roster, man.Id, Deed.Murder, 10);
            pipe.Away(prisoner);
            pipe.Freed(roster, prisoner, 12);

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
            Want(failures, pipe.EverEscaped(man.Id), "PIPE: the city remembers an escape.");
            Want(failures, pipe.Find(man.Id) == null, "PIPE: a freed man leaves the pipe.");

            // and the next judge adds it on
            var again = pipe.Book(roster, man.Id, Deed.Affray, 20);
            pipe.Away(again);
            pipe.Convicted(roster, again, 22);
            var clean = Sentencing.Days(Deed.Affray,
                new Random(Sentencing.StreamFor(roster.Seed, man.Id, 22)), true,
                man.Rank, Notability.Marked(man, 22), 0, 0);
            Want(failures, again.SentenceDays == clean,
                "PIPE: the surcharge reaches the sentence through the pipeline.");
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

        static void AMarkIsNeverDowngraded(List<string> failures)
        {
            var man = new Character { Id = 1, Surname = "Gallo" };
            WantedLevels.Mark(man, WantedLevels.CopKiller, 3);
            WantedLevels.Mark(man, WantedLevels.Fled, 4);
            Want(failures, man.WantedLevel == WantedLevels.CopKiller,
                "WANTED: running from an arrest must not demote a cop-killer.");

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

            var rang = 0;
            for (var seed = 0; seed < 200; seed++)
                if (ComplaintRoll.Rings(connected,
                        ComplaintRoll.StreamFor(1987, "deli", seed, 1)))
                    rang++;
            Want(failures, rang > 100,
                "COMPLAINT: over two hundred mornings a connected owner rings most of " +
                "them (" + rang + "/200).");
        }

        // ------------------------------------------------------------------ the trial

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
            Want(failures, walkedAway.Status == CaseStatus.Tried,
                "DOCKET: and a count folded in cannot be charged a second time.");
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
