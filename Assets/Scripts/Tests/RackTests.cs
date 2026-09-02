using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-103 / RACK-001 through RACK-016: who pays whom, what an
    /// owner weighs when he is asked, what a maybe is worth, and what it takes to make a
    /// street change hands. The ledger and the evaluation are pure, so all of it runs with
    /// the editor idle; the physical preconditions live in the runtime and are exercised
    /// in Play.
    /// </summary>
    public static class RackTests
    {
        static readonly TerritoryBusinessId Shop = new TerritoryBusinessId("biz:corner-shop");
        static readonly TerritoryBusinessId Bar = new TerritoryBusinessId("biz:bar");

        /// <summary>The block the corner shop stands on, for the tests that have to put
        /// real Fear on a real street before they ask the owner anything.</summary>
        static readonly TerritoryBlockId Street = new TerritoryBlockId("block:corner");

        public static List<string> Run()
        {
            var failures = new List<string>();

            AShopStandsWithEachHouseSeparately(failures);
            TheOwnerWeighsFearPresenceAndTheOtherClaim(failures);
            AcceptingIsTheOnlyWayToBePaid(failures);
            AMaybeIsNotAYes(failures);
            ARefusalIsRememberedAndStamped(failures);
            LeaningOnAnOwnerAsksHimAgain(failures);
            ViolenceAtTheShopIsRecordedAgainstTheHouse(failures);
            AStreetChangesHandsOnlyUnderSustainedPressure(failures);
            EveryHouseUsesTheSameRules(failures);
            AnOrderNamesTheHouseThatFiledIt(failures);
            ADoorRefusesEveryHouseTheSameWay(failures);
            TheStreetsTroubleReachesTheShopNextDoor(failures);
            ThePlayerReadsWordsAboutAShop(failures);
            TheCardSaysWhereTheShopStands(failures);
            EverySurfaceOffersTheSameOrders(failures);
            NobodyRobsADoorThatPaysUs(failures);
            TheLadderTerminates(failures);
            ASlipIsStampedTheDayItHappened(failures);
            OneVisitFilesOneSlip(failures);
            MoneyReachesTheWireWithItsSum(failures);
            AShakedownWalksTheDoorsThatHaveNotAnswered(failures);
            TheHideoutIsNamedOverOurOwnDoor(failures);

            return failures;
        }

        // ------------------------------------------------------------------- RACK-001

        /// <summary>
        /// The relationship is per Business×Gang: a shop can be paying us and still have
        /// told the Falcones no, and none of it is a claim on the block.
        /// </summary>
        static void AShopStandsWithEachHouseSeparately(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();

            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Unaffiliated)
                failures.Add("RACK-001: a shop nobody has spoken to is not unaffiliated.");

            ledger.Approach(Shop, Gang(0), 1.0);
            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Approached)
                failures.Add("RACK-001: standing at the door left no record.");

            ledger.Demand(Shop, Gang(0), Strong(), 2.0, out _);
            ledger.Demand(Shop, Gang(7), Hopeless(), 2.0, out _);

            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Compliant ||
                ledger.StateOf(Shop, Gang(7)) != TerritoryProtectionState.Defiant)
                failures.Add("RACK-001: two houses cannot stand differently with one shop.");
            if (ledger.StateOf(Bar, Gang(0)) != TerritoryProtectionState.Unaffiliated)
                failures.Add("RACK-001: a relationship leaked to another shop.");

            var rows = new List<TerritoryProtectionRelationship>();
            ledger.CollectRelationships(Shop, rows);
            if (rows.Count != 2 || rows[0].GangId.Value != 0 || rows[1].GangId.Value != 7)
                failures.Add("RACK-001: the relationships are wrong or out of order.");
        }

        // ------------------------------------------------------------------- RACK-006

        /// <summary>
        /// The answer is made of what the street feels, who stands on it, what it has just
        /// been through and whose claim stands against the asking - weighted, thresholded,
        /// and the same twice.
        /// </summary>
        static void TheOwnerWeighsFearPresenceAndTheOtherClaim(List<string> failures)
        {
            var config = TerritoryRacketConfig.Default;

            var feared = TerritoryComplianceEvaluation.Evaluate(Strong(), config);
            if (feared.Verdict != TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-006: a feared house standing on the street was refused.");

            var nothing = TerritoryComplianceEvaluation.Evaluate(Hopeless(), config);
            if (nothing.Verdict != TerritoryComplianceVerdict.Refuse)
                failures.Add("RACK-006: a house with nothing behind it was not refused.");

            var middling = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(35f, 20f, 0f, 5f, 0f, false), config);
            if (middling.Verdict != TerritoryComplianceVerdict.Hesitate)
                failures.Add("RACK-006: a middling case did not make the owner waver.");

            // Determinism: the same street twice is the same answer twice.
            var again = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(35f, 20f, 0f, 5f, 0f, false), config);
            if (Off(middling.Score, again.Score) || middling.Verdict != again.Verdict)
                failures.Add("RACK-006: the same inputs gave two different answers.");

            // A dominant rival turns the same demand down.
            var contested = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(60f, 40f, 0f, 95f, 0f, false), config);
            if (contested.Verdict != TerritoryComplianceVerdict.Refuse)
                failures.Add("RACK-006: a dominant rival did not stiffen the owner's back.");

            // And the weights are what decide it: retune, and a marginal case flips.
            var lenient = new TerritoryRacketConfig(acceptAt: 20f, hesitateAt: 5f);
            if (TerritoryComplianceEvaluation.Evaluate(
                    new TerritoryComplianceInputs(35f, 20f, 0f, 5f, 0f, false), lenient)
                    .Verdict != TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-006: the thresholds are not what the evaluation reads.");

            // The family already being paid is not arguing with itself.
            var ours = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(50f, 30f, 0f, 0f, 90f, true), config);
            var theirs = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(50f, 30f, 0f, 0f, 90f, false), config);
            if (!(ours.Score > theirs.Score))
                failures.Add("RACK-006: a shop's own protector was counted against itself.");
        }

        // ------------------------------------------------------------------- RACK-007

        /// <summary>
        /// Accepting is the only door into being paid, it publishes the transition, and it
        /// moves no money - the economy is a later epic.
        /// </summary>
        static void AcceptingIsTheOnlyWayToBePaid(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            var changes = new List<TerritoryProtectionChange>();

            ledger.Demand(Shop, Gang(0), Strong(), 5.0, out var terms, changes);
            if (terms.Verdict != TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-007: the fixture did not produce an acceptance.");
            if (!ledger.TryGetProtector(Shop, out var protector) || protector != Gang(0))
                failures.Add("RACK-007: an accepted demand did not make the shop ours.");
            if (changes.Count != 1 || changes[0].Current != TerritoryProtectionState.Compliant)
                failures.Add("RACK-007: the transition was not published exactly once.");

            var counted = new List<TerritoryBusinessId> { Shop, Bar };
            ledger.Compliance(counted, out var compliant, out var total, out var share);
            if (compliant != 1 || total != 2 || Off(share, 50f, 0.01f))
                failures.Add("RACK-007: the street's compliance count is wrong.");

            // A second house cannot also be paid: accepting ends the old arrangement.
            changes.Clear();
            ledger.Demand(Shop, Gang(7), Strong(), 6.0, out _, changes);
            if (!ledger.TryGetProtector(Shop, out var now) || now != Gang(7))
                failures.Add("RACK-007: the shop did not change hands on a new acceptance.");
            if (ledger.StateOf(Shop, Gang(0)) == TerritoryProtectionState.Compliant)
                failures.Add("RACK-007: two houses are being paid by one shop.");
        }

        // ------------------------------------------------------------------- RACK-008

        /// <summary>A wavering shop is not a paying shop, and it can still be worked on.</summary>
        static void AMaybeIsNotAYes(List<string> failures)
        {
            var config = TerritoryRacketConfig.Default;
            var ledger = new TerritoryRacketLedger(config);

            ledger.Demand(Shop, Gang(0), Middling(), 3.0, out var terms);
            if (terms.Verdict != TerritoryComplianceVerdict.Hesitate ||
                ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Hesitant)
                failures.Add("RACK-008: a middling demand did not leave the owner wavering.");
            if (ledger.TryGetProtector(Shop, out _))
                failures.Add("RACK-008: a wavering shop was counted as paying.");

            ledger.Compliance(new List<TerritoryBusinessId> { Shop }, out var compliant,
                out _, out var share);
            if (compliant != 0)
                failures.Add("RACK-008: a wavering shop was counted as compliant.");
            if (Off(share, config.HesitantComplianceShare * 100f, 0.01f))
                failures.Add("RACK-008: a maybe is not worth its configured fraction.");

            // And it can be pushed either way afterwards.
            ledger.Demand(Shop, Gang(0), Strong(), 4.0, out _);
            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Compliant)
                failures.Add("RACK-008: a wavering owner could not be brought round.");
        }

        // ------------------------------------------------------------------- RACK-009

        /// <summary>
        /// A refusal is a fact with a time on it - the street judges the house by what it
        /// does next, and FEAR-010 measures its window from this stamp.
        /// </summary>
        static void ARefusalIsRememberedAndStamped(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            ledger.Demand(Shop, Gang(0), Hopeless(), 9.0, out _);

            if (!ledger.TryGetRelationship(Shop, Gang(0), out var row))
                failures.Add("RACK-009: the refusal left no relationship.");
            else
            {
                if (row.State != TerritoryProtectionState.Defiant)
                    failures.Add("RACK-009: a refusal did not leave the shop defiant.");
                if (Math.Abs(row.RefusedAt - 9.0) > 0.001)
                    failures.Add("RACK-009: the refusal was not stamped with its hour.");
                if (row.Demands != 1)
                    failures.Add("RACK-009: the demand was not counted.");
            }

            var history = new List<TerritoryRacketEntry>();
            ledger.CollectHistory(Shop, history);
            if (history.Count == 0 || history[0].What != "refused")
                failures.Add("RACK-009: the refusal is not in the shop's history.");
        }

        // ------------------------------------------------------------------- RACK-010

        /// <summary>Leaning on an owner marks him and puts the question again.</summary>
        static void LeaningOnAnOwnerAsksHimAgain(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            ledger.Demand(Shop, Gang(0), Middling(), 1.0, out _);
            ledger.Threaten(Shop, Gang(0), 2.0);

            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Intimidated)
                failures.Add("RACK-010: a threat left the owner exactly as he was.");
            if (!ledger.TryGetRelationship(Shop, Gang(0), out var row) || row.Threats != 1)
                failures.Add("RACK-010: the threat was not counted.");

            // A shop already paying is not walked back down the ladder by a threat.
            var paid = new TerritoryRacketLedger();
            paid.Demand(Bar, Gang(0), Strong(), 1.0, out _);
            paid.Threaten(Bar, Gang(0), 2.0);
            if (paid.StateOf(Bar, Gang(0)) != TerritoryProtectionState.Compliant)
                failures.Add("RACK-010: threatening a paying shop unmade the arrangement.");
        }

        // ------------------------------------------------------------------- RACK-011

        /// <summary>
        /// When a physical system resolves violence at a business, the racket records it
        /// against the house that did it. The fear it causes belongs to the fear ledger and
        /// is filed by the caller, not here.
        /// </summary>
        static void ViolenceAtTheShopIsRecordedAgainstTheHouse(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            var changes = new List<TerritoryProtectionChange>();
            ledger.Escalate(Shop, Gang(0), TerritoryEscalationKind.PropertyDamage, 4.0, changes);

            if (!ledger.TryGetRelationship(Shop, Gang(0), out var row) || row.Escalations != 1)
                failures.Add("RACK-011: the escalation was not recorded on the relationship.");
            if (row.State != TerritoryProtectionState.Intimidated)
                failures.Add("RACK-011: violence at the shop left the owner unmoved.");
            if (changes.Count != 1)
                failures.Add("RACK-011: the escalation published no transition.");

            var history = new List<TerritoryRacketEntry>();
            ledger.CollectHistory(Shop, history);
            if (history.Count == 0 || !history[0].What.Contains("propertydamage"))
                failures.Add("RACK-011: the escalation is not in the shop's history.");
        }

        // ------------------------------------------------------------------- RACK-014

        /// <summary>
        /// A shop changes hands only when a challenger has been far enough ahead for
        /// several ticks running. One loud afternoon does nothing.
        /// </summary>
        static void AStreetChangesHandsOnlyUnderSustainedPressure(List<string> failures)
        {
            var config = new TerritoryRacketConfig(switchMargin: 18f, switchTicks: 3);
            var ledger = new TerritoryRacketLedger(config);
            ledger.Demand(Shop, Gang(0), Strong(), 1.0, out _);

            if (ledger.PressTowardSwitch(Shop, Gang(7), true) != 1)
                failures.Add("RACK-014: the first tick of pressure was not counted.");
            if (ledger.PressTowardSwitch(Shop, Gang(7), false) != 0)
                failures.Add("RACK-014: pressure that let up was not forgotten.");

            var ticks = 0;
            for (var i = 0; i < config.SwitchTicks; i++)
                ticks = ledger.PressTowardSwitch(Shop, Gang(7), true);
            if (ticks < config.SwitchTicks)
                failures.Add("RACK-014: sustained pressure did not accumulate.");

            var changes = new List<TerritoryProtectionChange>();
            if (!ledger.Switch(Shop, Gang(7), 10.0, changes))
                failures.Add("RACK-014: the shop refused to change hands under pressure.");
            if (!ledger.TryGetProtector(Shop, out var now) || now != Gang(7))
                failures.Add("RACK-014: the challenger did not end up with the shop.");
            if (ledger.StateOf(Shop, Gang(0)) == TerritoryProtectionState.Compliant)
                failures.Add("RACK-014: the old protector is still being paid.");
            if (changes.Count != 2)
                failures.Add("RACK-014: both houses were not told the shop changed hands.");

            // A challenger with nobody being paid has nothing to take.
            var free = new TerritoryRacketLedger(config);
            if (free.Switch(Bar, Gang(7), 11.0))
                failures.Add("RACK-014: an unprotected shop was 'taken' from nobody.");
        }

        // ------------------------------------------------------------------- RIVAL-003

        /// <summary>
        /// EVERY ORDER SAYS WHOSE IT IS. The gateway is the one wall twenty-one
        /// families file through, so an order with nobody's name on it is refused
        /// there, before any executor is asked - and the player's name goes on his own
        /// through exactly one helper.
        /// </summary>
        static void AnOrderNamesTheHouseThatFiledIt(List<string> failures)
        {
            var blank = new CollectDuesCommand(
                TerritoryCommandNodeId.Crew(1), Street);
            if (blank.House.IsValid)
                failures.Add("RIVAL-003: an order was born with somebody's name on it.");

            var ours = Gameplay.PlayerCommands.Stamp(blank);
            if (!ours.House.IsValid ||
                ours.House.Value != Gangs.GangCatalog.PlayerGangId)
                failures.Add("RIVAL-003: the player's own stamp did not name his house.");

            // The same helper serves any house: a mind stamps its own the same way.
            var theirs = blank;
            theirs.House = Gang(9);
            if (theirs.House != Gang(9) || ours.House == theirs.House)
                failures.Add("RIVAL-003: an order cannot be filed by another house.");

            // And the stamp is a COPY - stamping one order never renames another.
            if (blank.House.IsValid)
                failures.Add("RIVAL-003: stamping an order changed the one it was made from.");
        }

        /// <summary>
        /// A DOOR REFUSES EVERY HOUSE THE SAME WAY. Tenure is a relation, not a
        /// property of the shop: the same door is Ours to whoever holds its paper and
        /// Rival to everybody else, so "we do not rob the takings we collect" is a
        /// sentence all twenty-one families hear about their own doors.
        /// </summary>
        static void ADoorRefusesEveryHouseTheSameWay(List<string> failures)
        {
            foreach (var type in new[]
                     {
                         Outfit.OrderType.Raid, Outfit.OrderType.SmashUp,
                         Outfit.OrderType.Torch, Outfit.OrderType.Bomb,
                         Outfit.OrderType.BuyPremises,
                     })
            {
                var ours = Outfit.DoorOrders.Refusal(type, Outfit.DoorTenure.Ours);
                var theirs = Outfit.DoorOrders.Refusal(type, Outfit.DoorTenure.Ours);
                if (ours != theirs || string.IsNullOrEmpty(ours))
                    failures.Add("RIVAL-003: " + type +
                                 " against a house's own paper is not refused.");
                if (Outfit.DoorOrders.Refusal(type, Outfit.DoorTenure.Rival) != null)
                    failures.Add("RIVAL-003: " + type +
                                 " against another house's door was refused.");
            }

            foreach (var type in new[]
                     {
                         Outfit.OrderType.Raid, Outfit.OrderType.SmashUp,
                         Outfit.OrderType.Torch, Outfit.OrderType.Bomb,
                     })
                if (Outfit.DoorOrders.Refusal(type, Outfit.DoorTenure.Paying) == null)
                    failures.Add("RIVAL-003: " + type +
                                 " against a door that pays a house was allowed.");
        }

        // ------------------------------------------------------------------- RACK-013

        /// <summary>
        /// Rivals run on the player's rules exactly. Mirror the conditions and the answer
        /// mirrors; nothing in the evaluation asks which family is asking.
        /// </summary>
        static void EveryHouseUsesTheSameRules(List<string> failures)
        {
            var config = TerritoryRacketConfig.Default;
            var ours = new TerritoryRacketLedger(config);
            var theirs = new TerritoryRacketLedger(config);

            ours.Demand(Shop, Gang(0), Strong(), 1.0, out var mine);
            theirs.Demand(Shop, Gang(9), Strong(), 1.0, out var rival);

            if (mine.Verdict != rival.Verdict || Off(mine.Score, rival.Score))
                failures.Add("RACK-013: the same street answered two houses differently.");
            if (ours.StateOf(Shop, Gang(0)) != theirs.StateOf(Shop, Gang(9)))
                failures.Add("RACK-013: the transitions are not the same for a rival.");
        }

        // ------------------------------------------------------------------- RACK-012

        /// <summary>
        /// What the street has just been through reaches the shop next door, through the
        /// one fear channel and no second gossip store: raise the block's trouble and a
        /// marginal owner comes round.
        /// </summary>
        static void TheStreetsTroubleReachesTheShopNextDoor(List<string> failures)
        {
            var config = TerritoryRacketConfig.Default;
            var quiet = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(40f, 26f, 0f, 6f, 0f, false), config);
            var shaken = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(40f, 26f, 90f, 6f, 0f, false), config);

            if (!(shaken.Score > quiet.Score))
                failures.Add("RACK-012: trouble on the street counted for nothing.");
            if (quiet.Verdict == TerritoryComplianceVerdict.Accept ||
                shaken.Verdict != TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-012: a street that had just seen trouble did not come round.");
        }

        // ------------------------------------------------------------------- RACK-015

        /// <summary>The player's view of a shop is words, and read-only.</summary>
        static void ThePlayerReadsWordsAboutAShop(List<string> failures)
        {
            var view = new TerritoryBusinessPresentation(
                Shop, "Corner Shop", "Downtown Block 01", "Paying us", "us",
                "Afraid", TerritoryOwnerTone.Fearful, true);

            if (view.Standing != "Paying us" || view.LocalSituation != "Afraid")
                failures.Add("RACK-015: the shop's card does not read in words.");

            var type = typeof(TerritoryBusinessPresentation);
            foreach (var property in type.GetProperties())
                if (property.CanWrite)
                    failures.Add("RACK-015: the player's view of a shop can be written to.");
            foreach (var property in type.GetProperties())
                if (property.PropertyType == typeof(float) || property.PropertyType == typeof(double))
                    failures.Add("RACK-015: the player's view of a shop exposes an exact value.");

            var words = TerritoryStandingVocabulary.Default;
            if (words.Describe(TerritoryProtectionState.Unaffiliated) ==
                words.Describe(TerritoryProtectionState.Compliant))
                failures.Add("RACK-015: paying and unaffiliated read the same to the player.");
        }

        // ------------------------------------------------------------- RACK-015 (card)

        /// <summary>
        /// The one line under a business title says where it stands with us, or names the
        /// house that holds it - and it never prints a number the player has not earned.
        /// </summary>
        static void TheCardSaysWhereTheShopStands(List<string> failures)
        {
            var words = TerritoryStandingVocabulary.Default;

            var ours = LivingCity.UI.BusinessIntention.Line(
                "Sal Moretti", 850,
                words.Describe(TerritoryProtectionState.Compliant), "us");
            if (!ours.Contains("Paying us") || !ours.Contains("Sal Moretti"))
                failures.Add("RACK-015: the card does not say a shop is paying us.");

            var theirs = LivingCity.UI.BusinessIntention.Line(
                "Sal Moretti", 850,
                words.Describe(TerritoryProtectionState.Unaffiliated), "Falcone");
            if (!theirs.Contains("Falcone"))
                failures.Add("RACK-015: a shop held by another house does not name it.");

            var refused = LivingCity.UI.BusinessIntention.Line(
                "Sal Moretti", 850,
                words.Describe(TerritoryProtectionState.Defiant), "");
            if (!refused.Contains("Refused us"))
                failures.Add("RACK-015: a shop that told us no does not say so.");

            // Nothing said, nothing invented: the line still reads without a standing.
            var blank = LivingCity.UI.BusinessIntention.Line("Sal Moretti", 850, "", "");
            if (!blank.Contains("Unprotected"))
                failures.Add("RACK-015: a shop with no standing at all lost its line.");
        }

        // ------------------------------------------------------------- EPIC 6.5 orders

        /// <summary>
        /// One list of what can be put to a shopkeeper, so the street card, the paper map
        /// and the ledger cannot offer different things. What is available follows the two
        /// facts that matter: where the shop stands with us, and whether our men are at
        /// its door.
        /// </summary>
        /// <summary>
        /// The block file's sheet and the order book's map ask ONE table what may be done
        /// to a door (Outfit.DoorOrders), so they cannot disagree. A shop that pays us is
        /// the case that used to split them: the sheet never offered to rob it, while the
        /// map checked only the deed book and would send a crew to rob, wreck, torch or
        /// bomb the premises whose tribute the outfit collects.
        /// </summary>
        static void NobodyRobsADoorThatPaysUs(List<string> failures)
        {
            var violence = new[]
            {
                Outfit.OrderType.Raid,
                Outfit.OrderType.SmashUp,
                Outfit.OrderType.Torch,
                Outfit.OrderType.Bomb,
            };

            for (var i = 0; i < violence.Length; i++)
            {
                if (Outfit.DoorOrders.Refusal(violence[i], Outfit.DoorTenure.Paying) == null)
                    failures.Add("ORDER: " + violence[i] + " was allowed against a shop " +
                                 "that pays us for peace.");
                if (Outfit.DoorOrders.Refusal(violence[i], Outfit.DoorTenure.Ours) == null)
                    failures.Add("ORDER: " + violence[i] + " was allowed against our own " +
                                 "premises.");
                if (Outfit.DoorOrders.Refusal(violence[i], Outfit.DoorTenure.Rival) != null)
                    failures.Add("ORDER: " + violence[i] + " was refused against a rival's " +
                                 "door.");
                if (Outfit.DoorOrders.Refusal(violence[i], Outfit.DoorTenure.Open) != null)
                    failures.Add("ORDER: " + violence[i] + " was refused against a door " +
                                 "nobody holds.");
            }

            // Standing a watch on the shop we are paid by is exactly what protection IS,
            // and the deed is still bought from a door that pays us.
            if (Outfit.DoorOrders.Refusal(
                    Outfit.OrderType.Guard, Outfit.DoorTenure.Paying) != null)
                failures.Add("ORDER: a paying shop could not be guarded.");
            if (Outfit.DoorOrders.Refusal(
                    Outfit.OrderType.BuyPremises, Outfit.DoorTenure.Paying) != null)
                failures.Add("ORDER: a paying shop could not be bought outright.");
            if (Outfit.DoorOrders.Refusal(
                    Outfit.OrderType.BuyPremises, Outfit.DoorTenure.Ours) == null)
                failures.Add("ORDER: our own premises were offered for sale to us.");
        }

        static void EverySurfaceOffersTheSameOrders(List<string> failures)
        {
            var rows = new List<TerritoryRacketOrder>();
            const Outfit.DoorTenure open = Outfit.DoorTenure.Open;

            // A place that carries no business has nothing to ask.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, false, true, true, 0, rows);
            if (rows.Count != 0)
                failures.Add("ORDER: a civic building offered a racket order.");

            // Nobody picked to send: every doorstep row still STANDS, faded, saying why.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, true, false, false, 0, rows);
            if (Offers(rows, TerritoryRacketIntent.Approach, true) ||
                Offers(rows, TerritoryRacketIntent.Demand, true))
                failures.Add("ORDER: men were sent with no crew picked.");
            // The work FILED with the office fades on the same fact. The wrecking, the
            // torch and the robbery are men walking somewhere too, and a key taken with
            // nobody behind it is refused a second later on a line the block file never
            // showed - which is exactly how the ledger's keys read dead while the street
            // card's worked.
            if (Offers(rows, Outfit.OrderType.SmashUp, true) ||
                Offers(rows, Outfit.OrderType.Torch, true) ||
                Offers(rows, Outfit.OrderType.Raid, true) ||
                Offers(rows, Outfit.OrderType.Guard, true) ||
                Offers(rows, Outfit.OrderType.BuyPremises, true))
                failures.Add("ORDER: a door was wrecked or watched with no crew picked.");
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Note.Length == 0)
                    failures.Add("ORDER: a faded row does not say why.");

            // Men elsewhere: every order can be given - the approach CARRIES the demand
            // or the threat to the door (the RACKUI-001 chain: ApproachBusinessCommand
            // takes the intent and the arrival resolves it), so from range the rows
            // read available with the walking note rather than faded.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, true, true, false, 0, rows);
            if (!Offers(rows, TerritoryRacketIntent.Approach, true))
                failures.Add("ORDER: the men could not be sent to the door.");
            if (!Offers(rows, TerritoryRacketIntent.Demand, true) ||
                !Offers(rows, TerritoryRacketIntent.Threaten, true))
                failures.Add("ORDER: a demand from range no longer walks to the door.");

            // RACKUI-002. The wrecking is part of the LADDER, not a separate trade: an
            // owner who only wavers under a threat is exactly the man a smashed front is
            // meant to settle, so it stands open from the first visit - never gated
            // behind a refusal the player has to earn first.
            if (!Offers(rows, Outfit.OrderType.SmashUp, true) ||
                !Offers(rows, Outfit.OrderType.Torch, true))
                failures.Add("ORDER: the shop could be asked but never touched.");

            // Men at the door: the conversations open, and walking up again does not.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Defiant, open, true, true, true, 0, rows);
            if (!Offers(rows, TerritoryRacketIntent.Demand, true) ||
                !Offers(rows, TerritoryRacketIntent.Threaten, true))
                failures.Add("ORDER: with the men at the door there is nothing to say.");
            if (Offers(rows, TerritoryRacketIntent.Approach, true))
                failures.Add("ORDER: the men were sent to a door they are standing at.");

            // A shop already paying us is COLLECTED from, never asked again (ECON-008):
            // the round is its live doorstep row, and the demand never lights. The
            // violence is closed too - we do not rob the takings we collect.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Compliant, Outfit.DoorTenure.Paying,
                true, true, true, 4000, rows);
            if (!Offers(rows, TerritoryRacketIntent.Collect, true))
                failures.Add("ORDER: a paying shop's card does not offer the round.");
            if (Offers(rows, TerritoryRacketIntent.Demand, true) ||
                Offers(rows, TerritoryRacketIntent.Threaten, true) ||
                Offers(rows, TerritoryRacketIntent.Approach, true))
                failures.Add("ORDER: a shop that already pays us was asked again.");
            if (Offers(rows, Outfit.OrderType.SmashUp, true) ||
                Offers(rows, Outfit.OrderType.Torch, true) ||
                Offers(rows, Outfit.OrderType.Raid, true))
                failures.Add("ORDER: the family was offered its own takings to wreck.");

            // A new arrangement has no money on its meter until midnight. The row
            // remains visible to explain the wait, but cannot send an empty round.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Compliant, Outfit.DoorTenure.Paying,
                true, true, true, 4000, rows, collectionDue: false,
                collectionNote: "nothing owed yet · dues accrue daily at midnight");
            if (Offers(rows, TerritoryRacketIntent.Collect, true))
                failures.Add("ORDER: an empty collection round was available before midnight.");
            var collect = rows.Find(row =>
                row.Kind == TerritoryDoorRowKind.Racket &&
                row.Intent == TerritoryRacketIntent.Collect);
            if (!collect.Note.Contains("midnight"))
                failures.Add("ORDER: the first collection wait did not name midnight.");

            // Our own paper: nothing hostile stands, but a guard on the door does.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4000, rows);
            if (Offers(rows, TerritoryRacketIntent.Demand, true) ||
                Offers(rows, Outfit.OrderType.SmashUp, true) ||
                Offers(rows, Outfit.OrderType.BuyPremises, true))
                failures.Add("ORDER: our own premises were shaken down or sold to us.");
            if (!Offers(rows, Outfit.OrderType.Guard, true))
                failures.Add("ORDER: our own door could not be guarded.");

            // Our own paper is also somewhere our own men can BE. The row that takes a
            // crew inside stands on our doors and on no others, and once they are in it
            // is the row that brings them out again - the same one list, so the street
            // card, the paper map and the block file cannot differ about the family's
            // own house.
            if (!Named(rows, TerritoryRacketOrders.MoveInLabel))
                failures.Add("ORDER: our own door would not take our own men.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, false, false, 4000, rows);
            if (Offers(rows, TerritoryQuartersMove.In, true))
                failures.Add("ORDER: men were moved in with no crew picked.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4000, rows,
                quarters: TerritoryQuartersState.Here);
            if (Named(rows, TerritoryRacketOrders.MoveInLabel) ||
                !Offers(rows, TerritoryQuartersMove.Out, true))
                failures.Add("ORDER: a crew already inside was offered the door again.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, true, true, false, 4000, rows);
            if (Named(rows, TerritoryRacketOrders.MoveInLabel) ||
                Named(rows, TerritoryRacketOrders.MoveOutLabel))
                failures.Add("ORDER: our men were housed in somebody else's premises.");

            // The deed carries its price on the row itself, so every surface prints the
            // same sum beside the same word - and a door with no price does not offer it.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, true, true, false, 4000, rows);
            if (!Offers(rows, Outfit.OrderType.BuyPremises, true) ||
                Cash(rows, Outfit.OrderType.BuyPremises) != 4000)
                failures.Add("ORDER: the asking price is not on the deed row.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, open, true, true, false, 0, rows);
            if (Offers(rows, Outfit.OrderType.BuyPremises, true))
                failures.Add("ORDER: premises with no asking price were offered for sale.");

            // Every row is named exactly once and carries words, whatever the situation.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Hesitant, open, true, true, true, 4000, rows);
            var seen = new List<string>();
            for (var i = 0; i < rows.Count; i++)
            {
                var key = rows[i].Kind + ":" +
                          (rows[i].Kind == TerritoryDoorRowKind.Racket
                              ? rows[i].Intent.ToString()
                              : rows[i].Job.ToString());
                if (seen.Contains(key))
                    failures.Add("ORDER: the same order is on the card twice.");
                seen.Add(key);
                if (string.IsNullOrEmpty(rows[i].Label))
                    failures.Add("ORDER: a row has no words on it.");
            }

            // RACKUI-003. The block file and the street card read THIS list and nothing
            // else, so the menu against one door is the same on both. The chain, the
            // wrecking, the robbery, the watch and the deed are all of it.
            if (!Named(rows, TerritoryRacketOrders.ApproachLabel) ||
                !Named(rows, TerritoryRacketOrders.DemandLabel) ||
                !Named(rows, TerritoryRacketOrders.ThreatenLabel) ||
                !Named(rows, TerritoryRacketOrders.CollectLabel) ||
                !Named(rows, TerritoryRacketOrders.SmashLabel) ||
                !Named(rows, TerritoryRacketOrders.TorchLabel) ||
                !Named(rows, TerritoryRacketOrders.RobLabel) ||
                !Named(rows, TerritoryRacketOrders.GuardLabel) ||
                !Named(rows, TerritoryRacketOrders.BuyLabel))
                failures.Add("ORDER: a door row the ledger has is missing from the list.");
        }

        /// <summary>
        /// GAN-235. THE HIDEOUT IS NAMED OVER OUR OWN DOOR. Any premises on the family's
        /// paper can be the one address a running man makes for - the headquarters, a shop
        /// bought outright, whatever we hold - and there is only ever one: the row turns
        /// round once it is named rather than being offered to itself again.
        ///
        /// Both halves are asserted, because a menu that refuses the wrong doors by
        /// refusing every door would pass the first half and be useless.
        /// </summary>
        static void TheHideoutIsNamedOverOurOwnDoor(List<string> failures)
        {
            var rows = new List<TerritoryRacketOrder>();

            // A door on somebody else's paper is never named: you cannot hide in a shop
            // you do not own.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Compliant, Outfit.DoorTenure.Paying,
                true, true, false, 4_000, rows);
            if (Named(rows, TerritoryRacketOrders.HideoutLabel))
                failures.Add("HIDEOUT: a door we do not hold was offered as a hideout.");

            // Ours: the men can move in, and it can be named.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4_000, rows);
            if (!Named(rows, TerritoryRacketOrders.MoveInLabel))
                failures.Add("HIDEOUT: our own door would not take our own men.");
            if (!Offers(rows, TerritoryHideoutMove.Make, true))
                failures.Add("HIDEOUT: a premises of ours could not be named the hideout.");

            // And once it IS the hideout the row turns round rather than vanishing.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4_000, rows, isHideout: true);
            if (Named(rows, TerritoryRacketOrders.HideoutLabel) ||
                !Offers(rows, TerritoryHideoutMove.Give, true))
                failures.Add("HIDEOUT: the hideout was offered to itself again.");

            // A SHUT PREMISES IS NO HIDEOUT. Smashed in or burned out, nobody can walk
            // through it, so it cannot be named one - and the row stands, faded, saying
            // why. Giving one up stays open, which is how a player gets out of it.
            var boarded = new TerritoryDoorClosure(
                true, "closed - windows in - reopens in 3 days", false, false, 0);
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4_000, rows, closure: boarded);
            if (Offers(rows, TerritoryHideoutMove.Make, true))
                failures.Add("HIDEOUT: a boarded-up premises was offered as a hideout.");
            if (!Named(rows, TerritoryRacketOrders.HideoutLabel))
                failures.Add("HIDEOUT: the row must stand, faded, and say why.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, Outfit.DoorTenure.Ours,
                true, true, false, 4_000, rows, closure: boarded, isHideout: true);
            if (!Offers(rows, TerritoryHideoutMove.Give, true))
                failures.Add("HIDEOUT: a shut hideout could not be given up.");
        }

        static bool Offers(
            List<TerritoryRacketOrder> rows, TerritoryHideoutMove move, bool available)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Hideout &&
                    rows[i].HideoutMove == move && rows[i].Available == available)
                    return true;
            return false;
        }

        static bool Offers(
            List<TerritoryRacketOrder> rows, TerritoryQuartersMove move, bool available)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Quarters &&
                    rows[i].Move == move && rows[i].Available == available)
                    return true;
            return false;
        }

        static bool Offers(
            List<TerritoryRacketOrder> rows, TerritoryRacketIntent intent, bool available)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Racket &&
                    rows[i].Intent == intent && rows[i].Available == available)
                    return true;
            return false;
        }

        static bool Offers(
            List<TerritoryRacketOrder> rows, Outfit.OrderType job, bool available)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Job &&
                    rows[i].Job == job && rows[i].Available == available)
                    return true;
            return false;
        }

        static int Cash(List<TerritoryRacketOrder> rows, Outfit.OrderType job)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Job && rows[i].Job == job)
                    return rows[i].Cash;
            return 0;
        }

        static bool Named(List<TerritoryRacketOrder> rows, string label)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Label == label)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------- RACK-013

        /// <summary>
        /// THE LADDER HAS A LAST RUNG. The whole doorstep chain is a threat of violence,
        /// and a threat nobody ever has to make good on is not a threat: a player who
        /// asks, leans, smashes and burns and is still told "he is wavering" is playing
        /// a game that cannot be won at the thing it is about.
        ///
        /// The rule this pins down is VIOLENCE FRIGHTENS, PRESENCE COLLECTS. A wrecked
        /// front folds an owner while the family that wrecked it is standing on his
        /// street; the same wrecked front with nobody there is a frightened man with
        /// nobody to pay, and he only wavers. Both halves are asserted, because a
        /// balance that makes the first true by making everything true is not a balance.
        ///
        /// It runs the REAL ledgers - the fear table's own impacts, the presence table's
        /// own weights, the compliance evaluation, the racket's own Demand - so it fails
        /// when any of those is retuned past the point where the chain still ends.
        /// </summary>
        static void TheLadderTerminates(List<string> failures)
        {
            // What a crew standing on a block is worth, out of the presence table itself
            // rather than a number chosen here: a lieutenant and two men, stationed.
            var presenceConfig = new TerritoryPresenceConfig();
            var crewOnTheStreet = presenceConfig.PointsPerContributor *
                presenceConfig.StationedWeight *
                (presenceConfig.LieutenantWeight + 2f * presenceConfig.HoodWeight);

            var config = TerritoryRacketConfig.Default;

            // A shop that has just said no to us. ownerShift and tierBar are the man's
            // own nerve and what his kind of place is worth (ECON-002/007): the pair
            // that turned a 34 into a refusal in the live city while every screen
            // printed the table's own 30.
            TerritoryComplianceVerdict Ask(
                float fearOfUs, float presenceOfUs, float ownerShift = 0f, float tierBar = 0f)
            {
                var ledger = new TerritoryRacketLedger(config);
                ledger.Demand(
                    Shop, Gang(0),
                    new TerritoryComplianceInputs(fearOfUs, presenceOfUs, 0f, 0f, 0f, false),
                    12.0, out var terms, null, ownerShift, tierBar);
                return terms.Verdict;
            }

            // What one act of a given weight leaves the SHOPKEEPER feeling about us -
            // his own memory of it plus the share of it the street carries (FEAR-007).
            float FearAfter(Outfit.OrderType act)
            {
                var fear = new TerritoryFearLedger(new TerritoryFearConfig());
                fear.Record(new TerritoryFearEvent(
                    Gang(0), Street,
                    act == Outfit.OrderType.Raid
                        ? TerritoryFearCategory.Assault
                        : TerritoryFearCategory.PropertyDamage,
                    Outfit.DoorOrders.ViolenceSeverity(act),
                    TerritoryFearVisibility.Public, 12.0, Shop));
                return fear.BusinessFear(Street, Shop, Gang(0), 12.0);
            }

            // The ladder walked to its end on one door: the front put in, then burnt.
            float FearAfterBoth()
            {
                var fear = new TerritoryFearLedger(new TerritoryFearConfig());
                fear.Record(new TerritoryFearEvent(
                    Gang(0), Street, TerritoryFearCategory.PropertyDamage,
                    Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.SmashUp),
                    TerritoryFearVisibility.Public, 12.0, Shop));
                fear.Record(new TerritoryFearEvent(
                    Gang(0), Street, TerritoryFearCategory.PropertyDamage,
                    Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.Torch),
                    TerritoryFearVisibility.Public, 12.0, Shop));
                return fear.BusinessFear(Street, Shop, Gang(0), 12.0);
            }

            float FearAfterThreat()
            {
                var fear = new TerritoryFearLedger(new TerritoryFearConfig());
                fear.Record(new TerritoryFearEvent(
                    Gang(0), Street, TerritoryFearCategory.Threat, config.ThreatSeverity,
                    TerritoryFearVisibility.Seen, 12.0, Shop));
                return fear.BusinessFear(Street, Shop, Gang(0), 12.0);
            }

            // The last rung: the front went in, and our men are on the street.
            if (Ask(FearAfter(Outfit.OrderType.SmashUp), crewOnTheStreet) !=
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a smashed front with our men on the street " +
                             "still would not fold the owner - the ladder has no last rung.");

            if (Ask(FearAfter(Outfit.OrderType.Torch), crewOnTheStreet) !=
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a BURNT OUT owner with our men on the street " +
                             "still would not pay.");

            if (Ask(FearAfter(Outfit.OrderType.Raid), crewOnTheStreet) !=
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a robbed owner with our men on the street " +
                             "still would not pay.");

            // And the other half of the rule: frightening a man buys nothing from a
            // street we do not stand on. He has nobody to pay.
            if (Ask(FearAfter(Outfit.OrderType.SmashUp), 0f) ==
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a shop paid a family that was not there.");

            // Leaning on him is a step, not the whole ladder: the same street, the same
            // men, a threat instead of a wrecked front, and he does not fold.
            if (Ask(FearAfterThreat(), crewOnTheStreet) == TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a threat alone folded him - then nothing after " +
                             "it means anything.");

            // The acts are ordered by what they cost the man they are done to.
            if (Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.Torch) <=
                Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.SmashUp) ||
                Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.Bomb) <=
                Outfit.DoorOrders.ViolenceSeverity(Outfit.OrderType.Torch))
                failures.Add("RACK-013: a firebomb is worth no more than a bat.");

            // A HARDER man at a BETTER place: a proud owner (+10) of a tier-two premises
            // (+8) does not fold to one wrecked front - and does fold when the ladder is
            // walked to its end. Without this the contract would pass on the one owner
            // the city never actually deals: the neutral one.
            const float proud = 10f;
            var tierTwo = TerritoryTierGuard.AcceptBar(2);
            if (Ask(FearAfter(Outfit.OrderType.SmashUp), crewOnTheStreet, proud, tierTwo) ==
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a proud owner of a better place folded to one " +
                             "wrecked front - then the ladder has no rungs above it.");

            if (Ask(FearAfterBoth(), crewOnTheStreet, proud, tierTwo) !=
                TerritoryComplianceVerdict.Accept)
                failures.Add("RACK-013: a proud owner smashed AND burnt out, with our " +
                             "men on his street, still would not pay - there is nothing " +
                             "left to do to him.");

            // And the terms say what HIS yes costs, so a surface can print it. A player
            // told only the score reads 34 against a table bar of 30 and cannot see why
            // he was refused.
            var priced = TerritoryComplianceEvaluation.Evaluate(
                new TerritoryComplianceInputs(0f, 0f, 0f, 0f, 0f, false),
                config, proud, tierTwo);
            if (Off(priced.AcceptAt, config.AcceptAt + proud + tierTwo, 0.01f))
                failures.Add("RACK-013: the terms do not carry the bar this owner is " +
                             "actually held to.");
            if (Off(priced.Short, priced.AcceptAt, 0.01f))
                failures.Add("RACK-013: the terms cannot say how far short he is.");

            // A wrecked shop is INTIMIDATED, never paying: violence frightens a man, it
            // does not sign him up. The demand is still the only door into compliance.
            var wrecked = new TerritoryRacketLedger(config);
            wrecked.Escalate(
                Shop, Gang(0), TerritoryEscalationKind.PropertyDamage, 12.0);
            if (wrecked.StateOf(Shop, Gang(0)) == TerritoryProtectionState.Compliant)
                failures.Add("RACK-013: a shop started paying without ever being asked.");
        }

        // ------------------------------------------------------------------- the wire

        /// <summary>
        /// A slip is stamped with the day the CAMPAIGN is on, not the day the city clock
        /// is on. The two are one apart - the clock counts from zero and the campaign
        /// from one - and while the dispatch used the clock's, every door slip filed
        /// today sorted under yesterday's incidents on the wire.
        /// </summary>
        static void ASlipIsStampedTheDayItHappened(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            ledger.Approach(Shop, Gang(0), 30.5);

            if (ledger.Dispatches.Count != 1)
            {
                failures.Add("WIRE: standing at a door filed no slip.");
                return;
            }

            var slip = ledger.Dispatches[0];
            if (slip.Day != 2)
                failures.Add("WIRE: a slip filed at hour 30.5 is stamped day " +
                             slip.Day + ", not day 2.");
            if (Off((float)slip.HourOfDay, 6.5f))
                failures.Add("WIRE: the slip's hour is " + slip.HourOfDay + ", not 06:30.");
        }

        /// <summary>
        /// One walk to a door is one line on the wire. The men arriving and the answer
        /// they got are the same visit; filing both put two slips seconds apart for one
        /// thing that happened, which is what a bare walk is for and a demand is not.
        /// </summary>
        static void OneVisitFilesOneSlip(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            ledger.Approach(Shop, Gang(0), 10.0, null, announce: false);

            if (ledger.Dispatches.Count != 0)
                failures.Add("WIRE: a silent approach still put a slip on the wire.");
            if (ledger.StateOf(Shop, Gang(0)) != TerritoryProtectionState.Approached)
                failures.Add("WIRE: a silent approach did not move the standing.");

            ledger.Demand(Shop, Gang(0), Strong(), 11.0, out _);
            if (ledger.Dispatches.Count != 1)
                failures.Add("WIRE: one demand walk filed " + ledger.Dispatches.Count +
                             " slips, not one.");

            // And a bare walk still announces itself: it is the whole of the news.
            ledger.Approach(Bar, Gang(0), 12.0);
            if (ledger.Dispatches.Count != 2)
                failures.Add("WIRE: a bare walk to a door said nothing.");
        }

        // ---------------------------------------------------------------- the money

        /// <summary>
        /// A short and a miss are the two collection results a boss has to be able to
        /// react to, so each reaches the wire with the SUM and the story the owner told.
        /// A door that pays in full says nothing: the round's own slip covers it.
        /// </summary>
        static void MoneyReachesTheWireWithItsSum(List<string> failures)
        {
            var ledger = new TerritoryRacketLedger();
            var before = ledger.Version;

            ledger.FileMoney(Shop, Gang(0), TerritoryDoorNews.PaidShort, 26.0,
                90, 240, TerritoryPaymentExcuse.BadWeek);
            if (ledger.Dispatches.Count != 1 || ledger.Version == before)
                failures.Add("MONEY: a short did not reach the wire.");

            var slip = ledger.Dispatches[0];
            if (slip.Amount != 90 || slip.Stops != 240 ||
                slip.Excuse != TerritoryPaymentExcuse.BadWeek)
                failures.Add("MONEY: the short lost its figures on the way.");

            var words = TerritoryStandingVocabulary.Default.Describe(slip, "The Grill", "");
            if (words != "THE GRILL CAME UP SHORT - $90 OF $240 · \"A BAD WEEK\"")
                failures.Add("MONEY: the short reads \"" + words + "\".");

            ledger.FileRound(new TerritoryBlockId("block-9"), Gang(0),
                TerritoryDoorNews.RoundBanked, 27.0, 410, 7, 2);
            var round = ledger.Dispatches[ledger.Dispatches.Count - 1];
            if (round.BlockId.Value != "block-9" || round.Amount != 410 ||
                round.Stops != 7 || round.Short != 2)
                failures.Add("MONEY: the round slip lost its figures.");
            var roundWords = TerritoryStandingVocabulary.Default.Describe(
                round, "", "Dock Street");
            if (roundWords != "THE ROUND ON DOCK STREET BANKED $410 · 7 DOORS, 2 SHORT")
                failures.Add("MONEY: the banked round reads \"" + roundWords + "\".");

            // And a money slip is stamped on the campaign's clock like every other.
            if (round.Day != 2)
                failures.Add("MONEY: a round slip filed at hour 27 is stamped day " +
                             round.Day + ".");
        }

        /// <summary>
        /// A shakedown walks the doors that have NOT answered us - never one that pays
        /// and never one that has already said no. Holding out is the other list: the
        /// men who said no, and the ones who have not said yes.
        /// </summary>
        static void AShakedownWalksTheDoorsThatHaveNotAnswered(List<string> failures)
        {
            if (!TerritoryShakedown.WorthAsking(TerritoryProtectionState.Unaffiliated, false) ||
                !TerritoryShakedown.WorthAsking(TerritoryProtectionState.Approached, false) ||
                !TerritoryShakedown.WorthAsking(TerritoryProtectionState.Hesitant, false) ||
                !TerritoryShakedown.WorthAsking(TerritoryProtectionState.Intimidated, false))
                failures.Add("SHAKEDOWN: a door that has not answered was passed over.");
            if (TerritoryShakedown.WorthAsking(TerritoryProtectionState.Compliant, false) ||
                TerritoryShakedown.WorthAsking(TerritoryProtectionState.Defiant, false))
                failures.Add("SHAKEDOWN: a door that has answered was asked again.");

            if (!TerritoryShakedown.IsHoldout(TerritoryProtectionState.Defiant, false) ||
                !TerritoryShakedown.IsHoldout(TerritoryProtectionState.Hesitant, false))
                failures.Add("SHAKEDOWN: a holdout was not counted as one.");
            if (TerritoryShakedown.IsHoldout(TerritoryProtectionState.Compliant, false))
                failures.Add("SHAKEDOWN: a paying door was leaned on.");

            // OUR OWN DOOR IS NEVER ON EITHER LIST. It has no protection to be sold and
            // it is not holding out on anybody: a sweep of the block the headquarters
            // stands on must walk past it.
            for (var s = 0; s < 6; s++)
            {
                var state = (TerritoryProtectionState)s;
                if (TerritoryShakedown.WorthAsking(state, true))
                    failures.Add("SHAKEDOWN: our own premises was shaken down (" +
                                 state + ").");
                if (TerritoryShakedown.IsHoldout(state, true))
                    failures.Add("SHAKEDOWN: our own premises was leaned on (" +
                                 state + ").");
            }

            // And the policy that decides whether the men lean on the spot.
            for (var policy = 0; policy <= 3; policy++)
            {
                var strict = policy >= (int)LivingCity.Personnel.CrewPolicy.Strict;
                if (TerritoryShakedown.ThreatenAfter(
                        TerritoryComplianceVerdict.Accept, policy))
                    failures.Add("SHAKEDOWN: a yes was leaned on at policy " + policy + ".");
                if (TerritoryShakedown.ThreatenAfter(
                        TerritoryComplianceVerdict.Refuse, policy) != strict)
                    failures.Add("SHAKEDOWN: policy " + policy +
                                 " handled a refusal wrongly.");
                if (TerritoryShakedown.ThreatenAfter(
                        TerritoryComplianceVerdict.Hesitate, policy) != strict)
                    failures.Add("SHAKEDOWN: policy " + policy +
                                 " handled a waverer wrongly.");
            }

            // And the standings the block file prints: first match wins, in order.
            TerritoryDoorStandings.Of(
                TerritoryProtectionState.Compliant, false, false, "", "", null,
                true, 240, 240, 3, 0, 12, "Thursdays",
                out var kind, out var line, out var owed, out var daysLate, out _);
            if (kind != TerritoryDoorStandings.Late || owed != 240 || daysLate != 2)
                failures.Add("STANDING: a door a week behind did not read as late (" +
                             line + ").");

            TerritoryDoorStandings.Of(
                TerritoryProtectionState.Compliant, false, false, "", "", null,
                true, 40, 240, 11, 0, 12, "Thursdays",
                out kind, out line, out _, out _, out _);
            if (kind != TerritoryDoorStandings.Paying)
                failures.Add("STANDING: a door square with us did not read as paying.");
            if (line != "pays us · $40 owed · collects thursdays")
                failures.Add("STANDING: a paying door reads \"" + line + "\".");

            TerritoryDoorStandings.Of(
                TerritoryProtectionState.Compliant, false, true,
                "shut · reopens day 9", "",
                null, true, 240, 240, 3, 0, 12, "Thursdays",
                out kind, out _, out _, out _, out _);
            if (kind != TerritoryDoorStandings.Shut)
                failures.Add("STANDING: a shut door read as something else.");

            TerritoryDoorStandings.Of(
                TerritoryProtectionState.Unaffiliated, false, false, "", "", null,
                false, 0, 0, -1, 0, 12, "",
                out kind, out _, out _, out _, out _);
            if (kind != TerritoryDoorStandings.Unvisited)
                failures.Add("STANDING: a door nobody has called on read as something else.");

            // And our own premises reads as nothing at all, so the page prints the
            // tenure phrase that says whose it is.
            TerritoryDoorStandings.Of(
                TerritoryProtectionState.Unaffiliated, true, false, "", "", null,
                false, 0, 0, -1, 0, 12, "",
                out kind, out line, out _, out _, out _);
            if (kind != TerritoryDoorStandings.Other || line.Length != 0)
                failures.Add("STANDING: our own door was given a racket standing.");
        }

        // ------------------------------------------------------------------- fixtures

        static TerritoryGangId Gang(int id) => new TerritoryGangId(id);

        /// <summary>A feared house standing heavily on the street, with nobody against it.</summary>
        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);

        static TerritoryComplianceInputs Middling() =>
            new TerritoryComplianceInputs(35f, 20f, 0f, 5f, 0f, false);

        /// <summary>Nobody has heard of them and nobody is standing there.</summary>
        static TerritoryComplianceInputs Hopeless() =>
            new TerritoryComplianceInputs(0f, 0f, 0f, 40f, 0f, false);

        static bool Off(float value, float expected, float tolerance = 0.001f) =>
            Math.Abs(value - expected) > tolerance;
    }
}
