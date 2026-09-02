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
            ("AnEscapeCostsHimTwoDaysMore", AnEscapeCostsHimTwoDaysMore),
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
                Want(failures, affray >= 3 && affray <= 5,
                    "SENTENCE: an affray is three to five days, not " + affray + ".");
            }
            for (var i = 0; i < 200; i++)
            {
                var murder = Sentencing.Days(Deed.Murder, rng, false);
                Want(failures, murder >= 6 && murder <= 10,
                    "SENTENCE: a murder is longer than an affray, not " + murder + ".");
            }
            Want(failures, Sentencing.IsLife(Sentencing.Days(Deed.CopKilling, rng, false)),
                "SENTENCE: killing a policeman is life.");
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

        static void AnEscapeCostsHimTwoDaysMore(List<string> failures)
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

            pipe.DayTick(11, wanted);
            Want(failures, wanted.Count == 0, "PIPE: nobody rides before his court day.");
            pipe.DayTick(12, wanted);
            Want(failures, wanted.Count == 1 && wanted[0] == prisoner,
                "PIPE: his court day puts him up for transfer.");

            pipe.Away(prisoner);
            pipe.Convicted(roster, prisoner, 12);
            Want(failures, prisoner.Stage == PrisonStage.Sentenced && prisoner.SentenceDays >= 6,
                "PIPE: the verdict lands when the transfer arrives.");
            Want(failures, man.BackOnDay == 12 + prisoner.SentenceDays,
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
                new Random(Sentencing.StreamFor(roster.Seed, man.Id, 22)), false);
            Want(failures, again.SentenceDays == clean + Sentencing.EscapeSurcharge,
                "PIPE: the surcharge reaches the sentence through the pipeline.");
        }

        static void NoCarNoConvoyAndHeWaitsADay(List<string> failures)
        {
            var roster = BookedRoster(out var man, out var pipe);
            var prisoner = pipe.Book(roster, man.Id, Deed.Affray, 10);
            var wanted = new List<Prisoner>();

            pipe.DayTick(12, wanted);
            Want(failures, wanted.Count == 1, "PIPE: he is due on his court day.");

            // the precinct has no car: he goes back in the cells and rides tomorrow
            pipe.BackToTheCells(prisoner, 12);
            Want(failures, prisoner.Stage == PrisonStage.Held && prisoner.CourtDay == 13,
                "PIPE: a transfer with no car waits a day rather than losing the man.");
            Want(failures, man.Status == CharacterStatus.Jailed && man.BackOnDay == 0,
                "PIPE: waiting is still being held.");

            pipe.DayTick(13, wanted);
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
    }
}
