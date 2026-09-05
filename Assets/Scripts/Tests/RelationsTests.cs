using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// RIVAL-007. Where twenty-one families stand with one another, what each is owed by
    /// each, and the one rule the street reads before anybody fires.
    ///
    /// The engagement contracts are written FROM the three sentences the ledger prints on
    /// the FAMILIES card. If the sentences and the rule ever disagree, one of them is a
    /// lie to the player, and this suite is where that is caught.
    /// </summary>
    public static class RelationsTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            AStanceBelongsToThePair(failures);
            EveryGrievanceHasItsPriceAndItsMemory(failures);
            TheLadderIsMonotone(failures);
            AHouseThatCannotPayForAWarSuesForPeace(failures);
            TheStreetReadsTheThreeSentences(failures);
            APeacePairFightsOnlyWhenProvoked(failures);
            NobodyReadsAnotherHousesBooks(failures);
            AKillingOnPaperStrikesTheRightManOff(failures);
            TheBorderIsAGrievanceAndNeverAWar(failures);

            return failures;
        }

        // ------------------------------------------------------------------- AI-007

        /// <summary>
        /// A13/A18. A day on the border is worth the table's figure per bordering
        /// block, it is directed, and it is CAPPED at the retake rung: geography alone
        /// carries a house to "take a door back off them" and stops there. Everything
        /// above has to be earned by acts.
        /// </summary>
        static void TheBorderIsAGrievanceAndNeverAWar(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var book = new HouseRelations(config);

            var added = book.NoteBorder(1, 2, 2);
            if (added != config.BorderPressurePerDay * 2 ||
                System.Math.Abs(book.Grievance(1, 2) - added) > 0.001f)
                failures.Add("RELATIONS-009: two bordering blocks were worth " + added +
                             ", not " + config.BorderPressurePerDay * 2 + ".");
            if (book.Grievance(2, 1) != 0f)
                failures.Add("RELATIONS-009: the border put a grudge on the wrong side.");

            for (var day = 0; day < 60; day++)
                book.NoteBorder(1, 2, 3);
            if (book.Grievance(1, 2) > config.BorderPressureCap + 0.001f)
                failures.Add("RELATIONS-009: the border alone climbed past the cap to " +
                             book.Grievance(1, 2) + ".");
            if (book.StepOf(1, 2) > LadderStep.RetakeBusiness)
                failures.Add("RELATIONS-009: geography alone reached " + book.StepOf(1, 2) +
                             "; a war needs acts.");

            // An act on top of the border DOES climb past it.
            book.Note(1, 2, GrievanceKind.ManKilled);
            if (!(book.Grievance(1, 2) > config.BorderPressureCap))
                failures.Add("RELATIONS-009: a killing on top of the border was capped too.");

            // And once past the cap the border adds nothing more.
            if (book.NoteBorder(1, 2, 3) != 0)
                failures.Add("RELATIONS-009: the border kept adding above the cap.");

            // A house with nothing to file files nothing.
            if (book.NoteBorder(1, 1, 3) != 0 || book.NoteBorder(1, 4, 0) != 0)
                failures.Add("RELATIONS-009: a border with oneself, or of no blocks, counted.");
        }

        // ------------------------------------------------------------------ RIVAL-007

        /// <summary>(a) Two houses cannot disagree about whether they are at war, and a
        /// change lands at midnight and not before.</summary>
        static void AStanceBelongsToThePair(List<string> failures)
        {
            var book = new HouseRelations();

            book.SetPending(3, 7, Stance.War);
            if (book.StanceBetween(3, 7) != Stance.Peace ||
                book.StanceBetween(7, 3) != Stance.Peace)
                failures.Add("RELATIONS-001: a war landed before midnight.");

            book.ApplyPending();
            if (book.StanceBetween(3, 7) != Stance.War ||
                book.StanceBetween(7, 3) != Stance.War)
                failures.Add("RELATIONS-001: the two houses disagree about their own war.");

            if (book.StanceBetween(3, 3) != Stance.Peace)
                failures.Add("RELATIONS-001: a house is at war with itself.");
        }

        /// <summary>(b) Each kind of wrong is worth what the table says, and a grudge
        /// fades on its own.</summary>
        static void EveryGrievanceHasItsPriceAndItsMemory(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var kinds = new[]
            {
                GrievanceKind.DoorAttacked, GrievanceKind.DoorSwitched,
                GrievanceKind.RoundLost, GrievanceKind.ManKilled,
                GrievanceKind.WarningIgnored, GrievanceKind.TributeUnpaid,
            };

            for (var i = 0; i < kinds.Length; i++)
            {
                var book = new HouseRelations(config);
                book.Note(1, 2, kinds[i]);
                var owed = book.Grievance(1, 2);
                if (System.Math.Abs(owed - config.AmountOf(kinds[i])) > 0.001f)
                    failures.Add("RELATIONS-002: " + kinds[i] + " was worth " + owed +
                                 ", not " + config.AmountOf(kinds[i]) + ".");

                // BEING OWED IS NOT MUTUAL. They did it to us; we did nothing to them.
                if (book.Grievance(2, 1) != 0f)
                    failures.Add("RELATIONS-002: " + kinds[i] +
                                 " put a grudge on the wrong side.");

                book.DayTick(1);
                if (!(book.Grievance(1, 2) < owed))
                    failures.Add("RELATIONS-002: a day of quiet did not fade " + kinds[i]);
            }
        }

        /// <summary>(c) A bigger grudge is never a smaller step, and each threshold is
        /// where the table says it is.</summary>
        static void TheLadderIsMonotone(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var last = LadderStep.Ignore;
            for (var owed = 0; owed <= 100; owed++)
            {
                var step = config.StepFor(owed);
                if (step < last)
                    failures.Add("RELATIONS-003: the ladder went backwards at " + owed +
                                 " (" + last + " → " + step + ").");
                last = step;
            }

            if (config.StepFor(config.DiplomaticWarningAt) != LadderStep.DiplomaticWarning ||
                config.StepFor(config.DiplomaticWarningAt - 1) != LadderStep.Ignore)
                failures.Add("RELATIONS-003: the first step is not at " +
                             config.DiplomaticWarningAt + ".");
            if (config.StepFor(config.KillCrewMemberAt) != LadderStep.KillCrewMember)
                failures.Add("RELATIONS-003: the last step is not at " +
                             config.KillCrewMemberAt + ".");
        }

        /// <summary>(d) A house at war it cannot pay for offers a truce, whatever it is
        /// owed; a house that can pay declares.</summary>
        static void AHouseThatCannotPayForAWarSuesForPeace(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;

            var broke = Endurance(config.MinWarDays - 1);
            if (!(broke < config.MinWarDays))
                failures.Add("RELATIONS-004: the fixture is not actually broke.");

            var rich = Endurance(config.MinWarDays * 3);
            if (!(rich >= config.MinWarDays))
                failures.Add("RELATIONS-004: the fixture cannot afford a war it should.");

            // The mind's own rule, exercised through a view rather than restated here.
            var view = Bench(9);
            view.Accounts.Safe = view.DailyPayroll * (config.MinWarDays * 3);
            var them = new TerritoryGangId(4);
            view.Rivals = new[] { them };
            view.StanceLook = other => Stance.Peace;
            view.LadderLook = other => LadderStep.AttackBusiness;
            view.EnduranceLook = other => 1;

            var intents = new List<HouseIntent>();
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (!Declared(intents, them, Stance.War))
                failures.Add("RELATIONS-004: a family owed shops and able to pay for a " +
                             "war did not declare one.");

            view.Accounts.Safe = view.DailyPayroll * (config.MinWarDays - 1);
            view.StanceLook = other => Stance.War;
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            // Since EPIC 42 (DIPL-002) the truce is OFFERED, not imposed: the intent
            // is a proposal to them, and their desk answers it.
            if (!Proposed(intents, them, ProposalKind.OfferTruce))
                failures.Add("RELATIONS-004: a family that cannot pay through its own " +
                             "war did not sue for peace.");
            if (Declared(intents, them, Stance.Truce))
                failures.Add("RELATIONS-004: a truce was imposed rather than offered.");
        }

        static bool Proposed(List<HouseIntent> intents, TerritoryGangId them,
            ProposalKind kind)
        {
            for (var i = 0; i < intents.Count; i++)
                if (intents[i].Kind == HouseIntentKind.Propose &&
                    intents[i].Proposal != null && intents[i].Proposal.Kind == kind &&
                    intents[i].Other == them)
                    return true;
            return false;
        }

        static int Endurance(int days) => HouseRelations.Endurance(days * 100, 100);

        static bool Declared(List<HouseIntent> intents, TerritoryGangId them, Stance stance)
        {
            for (var i = 0; i < intents.Count; i++)
                if (intents[i].Kind == HouseIntentKind.SetStance &&
                    intents[i].Other == them && intents[i].Stance == stance)
                    return true;
            return false;
        }

        /// <summary>
        /// (e) The rule the street reads IS the three sentences the ledger prints.
        /// Written from the sentences, one case each.
        /// </summary>
        static void TheStreetReadsTheThreeSentences(List<string> failures)
        {
            // "PEACE - no engagement. Your men and theirs pass in the street, claimed
            //  ground or not."
            if (Engagement.May(Stance.Peace, oursIsTheGround: true, provoked: false) ||
                Engagement.May(Stance.Peace, oursIsTheGround: false, provoked: false))
                failures.Add("RELATIONS-005: PEACE is not no engagement.");

            // "TRUCE - territorial. Their men engage yours caught inside THEIR
            //  territory, and yours engage theirs on YOURS. Neutral ground stays quiet."
            if (!Engagement.May(Stance.Truce, oursIsTheGround: true, provoked: false))
                failures.Add("RELATIONS-005: TRUCE does not hold our own ground.");
            if (Engagement.May(Stance.Truce, oursIsTheGround: false, provoked: false))
                failures.Add("RELATIONS-005: TRUCE does not leave neutral ground quiet.");

            // "WAR - on sight. Their men engage yours anywhere in the city."
            if (!Engagement.May(Stance.War, oursIsTheGround: false, provoked: false) ||
                !Engagement.May(Stance.War, oursIsTheGround: true, provoked: false))
                failures.Add("RELATIONS-005: WAR is not on sight.");
        }

        /// <summary>(g) A man being shot at turns and fires back, whatever the two
        /// houses have agreed. That is not a stance question.</summary>
        static void APeacePairFightsOnlyWhenProvoked(List<string> failures)
        {
            if (!Engagement.May(Stance.Peace, oursIsTheGround: false, provoked: true))
                failures.Add("RELATIONS-006: a man being shot at was not allowed to " +
                             "shoot back.");
            if (Engagement.May(Stance.Peace, oursIsTheGround: false, provoked: false))
                failures.Add("RELATIONS-006: peace started something.");
        }

        /// <summary>(h) Nobody reads another family's books. What a house believes about
        /// another's endurance is the truth through a haze, and the haze is the same
        /// every time it is asked.</summary>
        static void NobodyReadsAnotherHousesBooks(List<string> failures)
        {
            var truth = 30;
            var exact = 0;
            for (var day = 0; day < 40; day++)
            {
                var seen = HouseRelations.Estimate(truth, 1987, day, 1, 4);
                var again = HouseRelations.Estimate(truth, 1987, day, 1, 4);
                if (seen != again)
                    failures.Add("RELATIONS-007: the same question answered twice, " +
                                 "differently.");
                if (seen == truth)
                    exact++;
                if (seen < (int)(truth * 0.7f) - 1 || seen > (int)(truth * 1.3f) + 1)
                    failures.Add("RELATIONS-007: the estimate left the band (" + seen +
                                 " of " + truth + ").");
            }

            if (exact > 4)
                failures.Add("RELATIONS-007: " + exact + " of 40 estimates were the " +
                             "true figure - somebody is reading their books.");
        }

        /// <summary>(f) A killing that happens on paper strikes the named man off HIS
        /// OWN family's roster, and the family that lost him holds it against the family
        /// that ordered it.</summary>
        static void AKillingOnPaperStrikesTheRightManOff(List<string> failures)
        {
            var theirs = RosterSeeder.Generate(11, 4);
            var target = -1;
            for (var i = 0; i < theirs.Members.Count && target < 0; i++)
                if (!theirs.Members[i].Gone && theirs.Members[i].Rank == Rank.Hood)
                    target = theirs.Members[i].Id;

            if (target < 0)
            {
                failures.Add("RELATIONS-008: the fixture dealt nobody to shoot.");
                return;
            }

            var house = new House(4, theirs, new CampaignRunner { Seed = 11 });
            HouseOps.Kill(house, target);
            var man = theirs.Find(target);
            if (man == null || !man.Gone)
                failures.Add("RELATIONS-008: the named man is still on their books.");

            var book = new HouseRelations();
            book.Note(4, 0, GrievanceKind.ManKilled);
            if (book.StepOf(4, 0) < LadderStep.DiplomaticWarning)
                failures.Add("RELATIONS-008: losing a man moved nothing on the ladder.");
            if (book.Grievance(0, 4) != 0f)
                failures.Add("RELATIONS-008: the killer holds a grudge for his own work.");
        }

        // -------------------------------------------------------------------- the bench

        /// <summary>A house with books and nothing else - no city, no doors, no street.
        /// Enough for the tiers that are only about another family.</summary>
        static HouseView Bench(int gangId)
        {
            var roster = RosterSeeder.Generate(3, gangId);
            var runner = new CampaignRunner { Seed = 3, GangId = gangId };
            runner.OpenFirstSheet();
            return new HouseView
            {
                House = new TerritoryGangId(gangId),
                Roster = roster,
                Accounts = runner.Accounts,
                Book = runner.Book,
                GameHour = 100.0,
                Day = 5,
            };
        }
    }
}
