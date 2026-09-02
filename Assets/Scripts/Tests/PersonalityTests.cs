using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 13's contract: what a man is LIKE, as opposed to what he is good at.
    /// Rolled once, never practised, never shown as a number, and never moved by
    /// anything that cannot say why.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class PersonalityTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("SameSeedSameCharacters", SameSeedSameCharacters),
            ("NeighboursAreNotTheSameMan", NeighboursAreNotTheSameMan),
            ("TheSpreadIsWideAndHasItsExtremes", TheSpreadIsWideAndHasItsExtremes),
            ("LoyaltyIsNotRolledTwice", LoyaltyIsNotRolledTwice),
            ("EveryTraitHasAWordAtEveryValue", EveryTraitHasAWordAtEveryValue),
            ("NothingMovesWithoutAReason", NothingMovesWithoutAReason),
            ("TheScaleHoldsAtBothEnds", TheScaleHoldsAtBothEnds),
            ("TheCowardFreezesAndSometimesRuns", TheCowardFreezesAndSometimesRuns),
            ("TheSteadyManNeverBreaks", TheSteadyManNeverBreaks),
            ("TheHotHeadTurnsACollectionIntoAShooting", TheHotHeadTurnsACollectionIntoAShooting),
            ("TheUndisciplinedTakeTheirOwnChoices", TheUndisciplinedTakeTheirOwnChoices),
            ("OneNightOneStoryAboutAMan", OneNightOneStoryAboutAMan),
            ("EveryIncidentIsReadyForThePaper", EveryIncidentIsReadyForThePaper),
            ("AGapOpensOnlyUnderABargain", AGapOpensOnlyUnderABargain),
            ("TheGreedyClimbTheLadderOnSchedule", TheGreedyClimbTheLadderOnSchedule),
            ("AWellPaidManIsQuietHoweverGreedy", AWellPaidManIsQuietHoweverGreedy),
            ("TheSkimIsMoneyThatIsActuallyGone", TheSkimIsMoneyThatIsActuallyGone),
            ("ARaiseClosesItAndARefusalCosts", ARaiseClosesItAndARefusalCosts),
            ("TheMorningPaperCarriesLastNight", TheMorningPaperCarriesLastNight),
            ("TheYearsReachThePaperOnceAMan", TheYearsReachThePaperOnceAMan),
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

        // ------------------------------------------------------------- the seeding

        static void SameSeedSameCharacters(List<string> failures)
        {
            var first = RosterSeeder.GenerateStaffed(1987);
            var second = RosterSeeder.GenerateStaffed(1987);

            for (var i = 0; i < first.Members.Count; i++)
                for (var t = 0; t < Personality.All.Length; t++)
                {
                    var trait = Personality.All[t];
                    var a = Personality.Get(first.Members[i], trait);
                    var b = Personality.Get(second.Members[i], trait);
                    if (a != b)
                        failures.Add($"SameSeedSameCharacters: {first.Members[i].FullName}" +
                                     $"'s {trait} was {a} then {b}.");
                }

            var other = RosterSeeder.GenerateStaffed(1988);
            var identical = true;
            for (var i = 0; i < first.Members.Count && identical; i++)
                for (var t = 0; t < Personality.All.Length; t++)
                    if (Personality.Get(first.Members[i], Personality.All[t]) !=
                        Personality.Get(other.Members[i], Personality.All[t]))
                    {
                        identical = false;
                        break;
                    }
            if (identical)
                failures.Add("SameSeedSameCharacters: seed 1988 dealt seed 1987's men.");
        }

        static void NeighboursAreNotTheSameMan(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 60);
            for (var i = 1; i < roster.Members.Count; i++)
            {
                var previous = roster.Members[i - 1];
                var member = roster.Members[i];
                if (previous.Rank == Rank.Boss || member.Rank == Rank.Boss)
                    continue;

                var same = true;
                for (var t = 0; t < Personality.All.Length && same; t++)
                {
                    var trait = Personality.All[t];
                    if (trait == PersonalityTrait.Loyalty)
                        continue;
                    if (Personality.Get(previous, trait) != Personality.Get(member, trait))
                        same = false;
                }
                if (same)
                    failures.Add($"NeighboursAreNotTheSameMan: {previous.FullName} and " +
                                 $"{member.FullName} came out of the same stream.");
            }
        }

        static void TheSpreadIsWideAndHasItsExtremes(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 1_000);
            var counted = 0;
            var outsideTheBand = 0;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Boss)
                    continue;

                for (var t = 0; t < Personality.All.Length; t++)
                {
                    var trait = Personality.All[t];
                    if (trait == PersonalityTrait.Loyalty)
                        continue;

                    var value = Personality.Get(member, trait);
                    if (value < 0 || value > 100)
                        failures.Add($"TheSpreadIsWide: {member.FullName}'s {trait} is " +
                                     $"{value}, off the scale entirely.");
                    counted++;
                    if (value < Personality.MinRoll || value > Personality.MaxRoll)
                        outsideTheBand++;
                }
            }

            // Personality is where men differ most, and an outfit with nobody in it who
            // is either a coward or a maniac is an outfit with no stories in it.
            var percent = counted > 0 ? outsideTheBand * 100 / counted : 0;
            if (percent < 2 || percent > 12)
                failures.Add($"TheSpreadIsWide: {percent}% of traits landed outside " +
                             $"{Personality.MinRoll}-{Personality.MaxRoll}, and the roll " +
                             $"asks for about {Personality.ExtremePercent}%.");
        }

        static void LoyaltyIsNotRolledTwice(List<string> failures)
        {
            // The seeder dealt Loyalty in its own narrower band before this system
            // existed, and re-rolling it here would re-deal every campaign's six.
            var roster = RosterSeeder.GenerateStaffed(1987);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Boss)
                    continue;
                if (member.Loyalty < 35 || member.Loyalty > 85)
                    failures.Add($"LoyaltyIsNotRolledTwice: {member.FullName} is on " +
                                 $"{member.Loyalty}, outside the seeder's own band - " +
                                 "something re-rolled him.");
            }
        }

        // -------------------------------------------------------------- the reading

        static void EveryTraitHasAWordAtEveryValue(List<string> failures)
        {
            foreach (PersonalityTrait trait in Enum.GetValues(typeof(PersonalityTrait)))
            {
                if (Personality.Label(trait).Length == 0)
                    failures.Add($"EveryTraitHasAWord: {trait} has no label.");

                var seen = new HashSet<string>();
                for (var value = 0; value <= 100; value++)
                {
                    var word = Personality.Band(trait, value);
                    if (word.Length == 0)
                        failures.Add($"EveryTraitHasAWord: {trait} at {value} has no word.");
                    seen.Add(word);
                }
                if (seen.Count < 5)
                    failures.Add($"EveryTraitHasAWord: {trait} only ever says " +
                                 $"{seen.Count} different things across the whole scale.");
            }
        }

        // -------------------------------------------------------------- the one door

        static void NothingMovesWithoutAReason(List<string> failures)
        {
            var man = new Character { Id = 7, FirstName = "Enzo", Surname = "Bardi" };
            var log = new List<PersonalityChange>();

            var before = man.Temper;
            var change = RosterOps.NudgePersonality(man, PersonalityTrait.Temper, 12,
                "watched his brother go down on the corner", log);

            if (man.Temper != before + 12)
                failures.Add($"NothingMovesWithoutAReason: Temper went {before} -> " +
                             $"{man.Temper} on a nudge of 12.");
            if (log.Count != 1 || log[0].Reason.Length == 0 ||
                log[0].CharacterId != man.Id || log[0].Delta != 12)
                failures.Add("NothingMovesWithoutAReason: the movement was not recorded " +
                             "with its reason.");
            if (change.To != man.Temper)
                failures.Add("NothingMovesWithoutAReason: the returned record disagrees " +
                             "with the man.");

            // A nudge that moves nothing records nothing: the feed prints what
            // happened, not what was attempted.
            RosterOps.NudgePersonality(man, PersonalityTrait.Temper, 0, "nothing at all",
                log);
            if (log.Count != 1)
                failures.Add("NothingMovesWithoutAReason: a nudge of zero got a line in " +
                             "the paper.");

            // Loyalty moves through the same door and is not a second field.
            var loyaltyBefore = man.Loyalty;
            RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, -5, "paid late", log);
            if (man.Loyalty != loyaltyBefore - 5)
                failures.Add("NothingMovesWithoutAReason: the nudge did not reach the " +
                             "one Loyalty field.");
        }

        // ----------------------------------------------------------- the checks fire

        /// <summary>A crew of men all built the same way, and a job to send them on.
        /// Returns the incidents of running that job a scripted number of times, on a
        /// fresh crew each time so a desertion cannot empty the fixture.</summary>
        static List<Incident> RunJobs(OrderType type, PersonalityTrait trait, int value,
            int times, out int deserted)
        {
            var incidents = new List<Incident>();
            deserted = 0;

            for (var run = 0; run < times; run++)
            {
                var roster = new Roster();
                var lieutenant = new Character
                {
                    Id = roster.NextCharacterId(), FirstName = "Lt", Surname = "Bruno",
                    Rank = Rank.Lieutenant,
                };
                roster.Members.Add(lieutenant);
                var crew = new Crew
                {
                    Id = roster.NextCrewId(), LieutenantId = lieutenant.Id,
                };
                var hood = new Character
                {
                    Id = roster.NextCharacterId(), FirstName = "Man", Surname = "Ferri",
                };
                roster.Members.Add(hood);
                crew.HoodIds.Add(hood.Id);
                roster.Crews.Add(crew);

                // Everybody steady except the one trait under test, so only that trait
                // can fire and the count means what it says.
                foreach (var man in new[] { lieutenant, hood })
                {
                    Personality.Set(man, PersonalityTrait.Courage, 90);
                    Personality.Set(man, PersonalityTrait.Temper, 10);
                    Personality.Set(man, PersonalityTrait.Discipline, 90);
                    Personality.Set(man, trait, value);
                }

                var job = new Job
                {
                    Id = run, CrewId = crew.Id, Type = type, Men = 2,
                    IssuedDay = 10 + run, TargetLabel = "Pearl Street",
                };
                var rng = new System.Random(1987 + run);
                OrderResolution.Resolve(OrderTable.SpecOf(type), job, roster, crew, rng,
                    null, incidents);

                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Status == CharacterStatus.Deserted)
                        deserted++;
            }

            return incidents;
        }

        static void TheCowardFreezesAndSometimesRuns(List<string> failures)
        {
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 0, 60,
                out var deserted);

            var froze = 0;
            var fled = 0;
            for (var i = 0; i < incidents.Count; i++)
            {
                if (incidents[i].Kind == IncidentKind.Froze) froze++;
                if (incidents[i].Kind == IncidentKind.Fled) fled++;
            }

            if (froze == 0)
                failures.Add("TheCowardFreezesAndSometimesRuns: sixty raids and a man " +
                             "with no nerve at all never once went to pieces.");
            if (fled == 0)
                failures.Add("TheCowardFreezesAndSometimesRuns: nobody ever ran.");
            if (deserted != fled)
                failures.Add($"TheCowardFreezesAndSometimesRuns: {fled} men ran and " +
                             $"{deserted} were struck off - a man who runs is off the " +
                             "books.");
        }

        static void TheSteadyManNeverBreaks(List<string> failures)
        {
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 90, 60,
                out var deserted);
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Froze ||
                    incidents[i].Kind == IncidentKind.Fled)
                    failures.Add("TheSteadyManNeverBreaks: a man well over the floor was " +
                                 "still rolled for, and broke.");
            if (deserted != 0)
                failures.Add("TheSteadyManNeverBreaks: a steady man deserted.");
        }

        static void TheHotHeadTurnsACollectionIntoAShooting(List<string> failures)
        {
            var incidents = RunJobs(OrderType.CollectProtection, PersonalityTrait.Temper,
                100, 60, out _);

            var escalated = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Escalated)
                {
                    escalated++;
                    if (incidents[i].Heat <= 0)
                        failures.Add("TheHotHead: a shooting nobody ordered drew no " +
                                     "police attention at all.");
                }
            if (escalated == 0)
                failures.Add("TheHotHead: sixty collections by a man with no temper " +
                             "control and not one of them turned.");

            // And the placid man never escalates.
            var calm = RunJobs(OrderType.CollectProtection, PersonalityTrait.Temper, 10,
                60, out _);
            for (var i = 0; i < calm.Count; i++)
                if (calm[i].Kind == IncidentKind.Escalated)
                    failures.Add("TheHotHead: a placid man started a gunfight.");
        }

        static void TheUndisciplinedTakeTheirOwnChoices(List<string> failures)
        {
            var incidents = RunJobs(OrderType.CollectProtection,
                PersonalityTrait.Discipline, 0, 60, out _);

            var deviated = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Deviated)
                    deviated++;
            if (deviated == 0)
                failures.Add("TheUndisciplinedTakeTheirOwnChoices: a man with no " +
                             "discipline did sixty jobs exactly as written.");

            var exact = RunJobs(OrderType.CollectProtection, PersonalityTrait.Discipline,
                90, 60, out _);
            for (var i = 0; i < exact.Count; i++)
                if (exact[i].Kind == IncidentKind.Deviated)
                    failures.Add("TheUndisciplinedTakeTheirOwnChoices: an exact man " +
                                 "improvised.");
        }

        static void OneNightOneStoryAboutAMan(List<string> failures)
        {
            // A man who went to pieces is not then also written up for improvising.
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 0, 60,
                out _);
            var seen = new Dictionary<string, int>();
            for (var i = 0; i < incidents.Count; i++)
            {
                var key = incidents[i].Day + ":" + incidents[i].CharacterId;
                seen.TryGetValue(key, out var count);
                seen[key] = count + 1;
                if (count + 1 > 1)
                    failures.Add($"OneNightOneStoryAboutAMan: {incidents[i].Name} got " +
                                 $"{count + 1} write-ups out of one job on day " +
                                 $"{incidents[i].Day}.");
            }
        }

        static void EveryIncidentIsReadyForThePaper(List<string> failures)
        {
            foreach (IncidentKind kind in Enum.GetValues(typeof(IncidentKind)))
            {
                var line = PersonalityChecks.Line(kind, "Rocco Vale", "Pearl Street");
                if (line.Length == 0)
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind} has no line.");
                if (!line.Contains("Rocco Vale"))
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind}'s line does " +
                                 "not name the man it is about.");
                // Only the things that happen SOMEWHERE name a street. A man taking
                // rival money, asking for a raise or turning forty-seven did not do it
                // on a corner.
                var placed = kind == IncidentKind.Froze || kind == IncidentKind.Fled ||
                             kind == IncidentKind.Escalated ||
                             kind == IncidentKind.Deviated ||
                             kind == IncidentKind.CaughtSkimming;
                if (placed && !line.Contains("Pearl Street"))
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind}'s line does " +
                                 "not say where it happened.");

                // And it still reads when nobody could say where.
                if (PersonalityChecks.Line(kind, "Rocco Vale", "").Length == 0)
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind} has no line " +
                                 "when the place is unknown.");
            }
        }

        // ------------------------------------------------------------- greed and pay

        /// <summary>A five-star lieutenant who signed for HALF the house rate - a man
        /// out of the classified column whose stars have long outgrown the price he
        /// printed. WAGE-001 made the bargain the only thing a pay gap can come from,
        /// so this is now what an underpaid man has to be built out of.</summary>
        static Character Underpaid(Roster roster, int greed)
        {
            var man = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Rocco", Surname = "Vale",
                Rank = Rank.Lieutenant,
            };
            for (var s = 0; s < AttributeScale.Count; s++)
                man.SetHalfSteps((CharacterAttribute)s, AttributeScale.MaxHalfSteps);
            Personality.Set(man, PersonalityTrait.Greed, greed);
            man.WageAsked = Wages.HouseRate(man) / 2;
            roster.Members.Add(man);
            return man;
        }

        /// <summary>
        /// WAGE-001. ONE table: what the house pays IS what the man is worth, so
        /// nobody on the house scale can be underpaid at any rank. A gap exists only
        /// where a BARGAIN sits under the rate.
        /// </summary>
        static void AGapOpensOnlyUnderABargain(List<string> failures)
        {
            var roster = new Roster();

            var lieutenant = new Character
            {
                Id = roster.NextCharacterId(), Rank = Rank.Lieutenant,
            };
            for (var s = 0; s < AttributeScale.Count; s++)
                lieutenant.SetHalfSteps((CharacterAttribute)s, AttributeScale.MaxHalfSteps);
            roster.Members.Add(lieutenant);
            if (Wages.PayGap(lieutenant) != 0)
                failures.Add($"AGapOpensOnlyUnderABargain: a five-star lieutenant on " +
                             $"the house scale reads {Wages.PayGap(lieutenant)} short " +
                             "of it, so every lieutenant is underpaid from day one again.");

            // A plain hood on the house scale is paid exactly what he is worth.
            var hood = new Character { Id = roster.NextCharacterId() };
            for (var s = 0; s < AttributeScale.Count; s++)
                hood.SetHalfSteps((CharacterAttribute)s, 7);
            roster.Members.Add(hood);
            if (Wages.PayGap(hood) != 0)
                failures.Add($"AGapOpensOnlyUnderABargain: a hood on the house " +
                             $"scale reads {Wages.PayGap(hood)} short of it.");

            // And the one man who CAN be short: a bargain under the rate.
            var bargained = Underpaid(roster, 50);
            if (Wages.PayGap(bargained) <= 0)
                failures.Add("AGapOpensOnlyUnderABargain: a man drawing half the house " +
                             "rate is not short of anything, so nobody can ever be " +
                             "underpaid.");
        }

        static void TheGreedyClimbTheLadderOnSchedule(List<string> failures)
        {
            var roster = new Roster();
            var man = Underpaid(roster, 90);

            var incidents = new List<Incident>();
            var changes = new List<PersonalityChange>();
            var loyaltyBefore = man.Loyalty;
            var skimStartedOn = -1;
            var rivalOn = -1;
            var demandOn = -1;

            for (var day = 1; day <= 60; day++)
            {
                var wasSkimming = man.Skimming;
                var count = incidents.Count;
                GreedLadder.Tick(man, Wages.WageFor(man), Wages.WorthOf(man), day, incidents, changes);

                if (!wasSkimming && man.Skimming)
                    skimStartedOn = day;
                for (var i = count; i < incidents.Count; i++)
                {
                    if (incidents[i].Kind == IncidentKind.TookRivalMoney) rivalOn = day;
                    if (incidents[i].Kind == IncidentKind.DemandedARaise) demandOn = day;
                }
            }

            // The clock starts the first day he is short, so each rung lands one day
            // after its stated wait.
            if (skimStartedOn != 1 + GreedLadder.SkimAfterDays)
                failures.Add($"TheGreedyClimbTheLadder: he started skimming on day " +
                             $"{skimStartedOn}, not {1 + GreedLadder.SkimAfterDays}.");
            if (rivalOn != 1 + GreedLadder.RivalAfterDays)
                failures.Add($"TheGreedyClimbTheLadder: rival money on day {rivalOn}, " +
                             $"not {1 + GreedLadder.RivalAfterDays}.");
            if (demandOn != 1 + GreedLadder.DemandAfterDays)
                failures.Add($"TheGreedyClimbTheLadder: he asked on day {demandOn}, not " +
                             $"{1 + GreedLadder.DemandAfterDays}.");
            if (man.Loyalty >= loyaltyBefore)
                failures.Add("TheGreedyClimbTheLadder: somebody else's money cost the " +
                             "outfit nothing.");
            if (changes.Count == 0 || changes[0].Reason.Length == 0)
                failures.Add("TheGreedyClimbTheLadder: the loyalty drop carried no reason.");
            if (man.WageDemand <= man.WageAsked)
                failures.Add("TheGreedyClimbTheLadder: he asked for no more than he was " +
                             "already getting.");

            // The same gap, the same days, a man who is not greedy: nothing at all.
            var second = new Roster();
            var content = Underpaid(second, 20);
            var quiet = new List<Incident>();
            for (var day = 1; day <= 60; day++)
                GreedLadder.Tick(content, Wages.WageFor(content), Wages.WorthOf(content), day, quiet, null);
            if (quiet.Count != 0 || content.Skimming || content.WageDemand != 0)
                failures.Add("TheGreedyClimbTheLadder: a man with no greed in him " +
                             "climbed the ladder anyway.");
        }

        static void AWellPaidManIsQuietHoweverGreedy(List<string> failures)
        {
            var roster = new Roster();
            var man = Underpaid(roster, 95);
            man.WageAsked = Wages.WorthOf(man);

            var incidents = new List<Incident>();
            for (var day = 1; day <= 90; day++)
                GreedLadder.Tick(man, Wages.WageFor(man), Wages.WorthOf(man), day, incidents, null);

            if (incidents.Count != 0 || man.Skimming || man.UnderpaidSince != 0)
                failures.Add("AWellPaidManIsQuietHoweverGreedy: he is paid the rate and " +
                             "still went looking.");
        }

        static void TheSkimIsMoneyThatIsActuallyGone(List<string> failures)
        {
            // Two identical collections, one crew clean and one with a hand in it.
            var honest = CollectionPayout(skimming: false);
            var short_ = CollectionPayout(skimming: true);

            if (honest <= 0)
                failures.Add("TheSkimIsMoneyThatIsActuallyGone: the clean collection " +
                             "paid nothing, so the comparison proves nothing.");
            if (short_ >= honest)
                failures.Add($"TheSkimIsMoneyThatIsActuallyGone: the crew with a skimmer " +
                             $"on it brought back {short_} against {honest} - the money " +
                             "is not actually missing.");
        }

        static int CollectionPayout(bool skimming)
        {
            var roster = new Roster();
            var lieutenant = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Lt", Rank = Rank.Lieutenant,
            };
            // Blind and disorganised, so the catch roll cannot change the money.
            lieutenant.SetHalfSteps(CharacterAttribute.Awareness, AttributeScale.MinHalfSteps);
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MinHalfSteps);
            Personality.Set(lieutenant, PersonalityTrait.Temper, 10);
            Personality.Set(lieutenant, PersonalityTrait.Discipline, 90);
            roster.Members.Add(lieutenant);

            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            var hood = new Character { Id = roster.NextCharacterId(), FirstName = "Man" };
            Personality.Set(hood, PersonalityTrait.Temper, 10);
            Personality.Set(hood, PersonalityTrait.Discipline, 90);
            hood.Skimming = skimming;
            roster.Members.Add(hood);
            crew.HoodIds.Add(hood.Id);
            roster.Crews.Add(crew);

            var job = new Job
            {
                Id = 1, CrewId = crew.Id, Type = OrderType.CollectProtection, Men = 2,
                IssuedDay = 5, TargetLabel = "Pearl Street", TargetWorth = 400,
            };
            var outcome = OrderResolution.Resolve(
                OrderTable.SpecOf(OrderType.CollectProtection), job, roster, crew,
                new System.Random(11), OrderOutcome.Completed, null);
            return outcome.Payout;
        }

        static void ARaiseClosesItAndARefusalCosts(List<string> failures)
        {
            var roster = new Roster();
            var man = Underpaid(roster, 90);
            var incidents = new List<Incident>();
            for (var day = 1; day <= 40; day++)
                GreedLadder.Tick(man, Wages.WageFor(man), Wages.WorthOf(man), day, incidents, null);

            if (man.WageDemand <= 0)
                failures.Add("ARaiseClosesIt: he never asked, so there is nothing to " +
                             "answer.");

            // WAGE-002. He asked for the rate and he is put ON the rate: the bargain
            // is torn up rather than moved to the figure he named, so his envelope
            // follows his stars from here like every man the outfit raised itself.
            var asked = man.WageDemand;
            var granted = RosterOps.GrantRaise(roster, man.Id);
            if (!granted.Ok || man.WageAsked != 0 || man.WageDemand != 0 ||
                man.Skimming || man.UnderpaidSince != 0)
                failures.Add("ARaiseClosesIt: saying yes did not close it.");
            if (Wages.WageFor(man) != asked)
                failures.Add($"ARaiseClosesIt: he asked {asked} and draws " +
                             $"{Wages.WageFor(man)}.");
            if (Wages.PayGap(man) != 0)
                failures.Add($"ARaiseClosesIt: he is still {Wages.PayGap(man)} short " +
                             "after being given exactly what he asked for.");

            // And the freeze is gone: he is on the SCALE now, not on a number, so his
            // envelope moves with a trade he is paid for. He is at the ceiling, so the
            // proof is a step DOWN - the old grant pinned the figure and would not have
            // moved either way.
            var onTheScale = Wages.WageFor(man);
            man.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MaxHalfSteps - 1);
            if (Wages.WageFor(man) != onTheScale - Wages.LieutenantPerHalfStep)
                failures.Add("ARaiseClosesIt: after the raise the wage no longer " +
                             "follows the stars - the old freeze is back.");

            // And the other answer.
            var second = new Roster();
            var other = Underpaid(second, 90);
            for (var day = 1; day <= 40; day++)
                GreedLadder.Tick(other, Wages.WageFor(other), Wages.WorthOf(other), day, incidents, null);
            var loyaltyBefore = other.Loyalty;
            var changes = new List<PersonalityChange>();
            var refused = RosterOps.RefuseRaise(second, other.Id, changes);
            if (!refused.Ok || other.Loyalty >= loyaltyBefore || changes.Count == 0)
                failures.Add("ARaiseClosesIt: saying no cost the outfit nothing.");
            if (other.UnderpaidSince == 0)
                failures.Add("ARaiseClosesIt: refusing him reset the clock, so he " +
                             "starts the whole ladder again from nothing.");

            // Nobody can be answered twice.
            if (RosterOps.RefuseRaise(second, other.Id).Ok)
                failures.Add("ARaiseClosesIt: he was refused a demand he had not made.");
        }

        // ------------------------------------------------------------------ the feed

        static void TheMorningPaperCarriesLastNight(List<string> failures)
        {
            var roster = new Roster();
            var runner = new CampaignRunner();

            runner.Incidents.Add(new Incident(1, "Rocco Vale", IncidentKind.Froze, 1,
                "Pearl Street", 0,
                IncidentText.Line(IncidentKind.Froze, "Rocco Vale", "Pearl Street")));

            runner.DayTick(roster);

            if (runner.LastNight.Count != 1)
                failures.Add($"TheMorningPaperCarriesLastNight: the page carries " +
                             $"{runner.LastNight.Count} lines against the one that " +
                             "happened.");
            if (runner.Incidents.Count != 0)
                failures.Add("TheMorningPaperCarriesLastNight: the desk was not cleared " +
                             "for the new day.");
            if (runner.IncidentBook.Count != 1 ||
                runner.IncidentBook[0].CharacterId != 1)
                failures.Add("TheMorningPaperCarriesLastNight: the book did not keep it.");

            // A quiet night prints a paper with no such column, and does not carry
            // yesterday's lines into it.
            runner.DayTick(roster);
            if (runner.LastNight.Count != 0)
                failures.Add("TheMorningPaperCarriesLastNight: a quiet night reprinted " +
                             "the night before.");
            if (runner.IncidentBook.Count != 1)
                failures.Add("TheMorningPaperCarriesLastNight: the book forgot, or " +
                             "double-counted.");

            // The window holds. Fill it past its limit and the oldest fall off the front.
            for (var i = 0; i < CampaignRunner.IncidentsKept + 20; i++)
            {
                runner.Incidents.Add(new Incident(i + 100, "Man " + i,
                    IncidentKind.Deviated, 3, "", 0, "line " + i));
                runner.DayTick(roster);
            }
            if (runner.IncidentBook.Count > CampaignRunner.IncidentsKept)
                failures.Add($"TheMorningPaperCarriesLastNight: the book is " +
                             $"{runner.IncidentBook.Count} deep and the window is " +
                             $"{CampaignRunner.IncidentsKept}.");
        }

        static void TheYearsReachThePaperOnceAMan(List<string> failures)
        {
            var roster = new Roster();
            var runner = new CampaignRunner();

            // A man who turns fifty-one on the campaign's own opening day-of-year, so
            // the first tick that reaches his birthday takes something off him.
            var man = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Aldo", Surname = "Vecchi",
                BirthYear = RosterSeeder.CalendarStartYear - 51,
                BirthDayOfYear = 4,
            };
            for (var s = 0; s < AttributeScale.Count; s++)
                man.SetHalfSteps((CharacterAttribute)s, AttributeScale.MaxHalfSteps);
            roster.Members.Add(man);

            var slowing = 0;
            for (var day = 0; day < 10; day++)
            {
                runner.DayTick(roster);
                for (var i = 0; i < runner.LastNight.Count; i++)
                    if (runner.LastNight[i].Kind == IncidentKind.SlowingDown)
                        slowing++;
            }

            if (slowing == 0)
                failures.Add("TheYearsReachThePaperOnceAMan: a man losing his hands on " +
                             "his fifty-second birthday never made the paper.");
            if (slowing > 1)
                failures.Add($"TheYearsReachThePaperOnceAMan: {slowing} lines about one " +
                             "man's birthday - three ways of saying he turned " +
                             "fifty-two is not three stories.");
        }

        static void TheScaleHoldsAtBothEnds(List<string> failures)
        {
            var man = new Character { Id = 1 };
            var log = new List<PersonalityChange>();

            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, 500, "test", log);
            if (man.Courage != 100)
                failures.Add($"TheScaleHoldsAtBothEnds: Courage went to {man.Courage}.");

            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, -500, "test", log);
            if (man.Courage != 0)
                failures.Add($"TheScaleHoldsAtBothEnds: Courage went to {man.Courage}.");

            // Already at the floor: nothing moves and nothing is printed.
            var lines = log.Count;
            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, -10, "test", log);
            if (log.Count != lines)
                failures.Add("TheScaleHoldsAtBothEnds: a man already at zero got a line " +
                             "about getting more frightened.");
        }
    }
}
