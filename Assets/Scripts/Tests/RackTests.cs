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
            TheStreetsTroubleReachesTheShopNextDoor(failures);
            ThePlayerReadsWordsAboutAShop(failures);
            TheCardSaysWhereTheShopStands(failures);
            EverySurfaceOffersTheSameOrders(failures);
            NobodyRobsADoorThatPaysUs(failures);

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

            // A place that carries no business has nothing to ask.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, false, true, true, rows);
            if (rows.Count != 0)
                failures.Add("ORDER: a civic building offered a racket order.");

            // Nobody picked to send: the approach row stands, faded, saying why.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, true, false, false, rows);
            if (rows.Count != 1 || rows[0].Available || rows[0].Note.Length == 0)
                failures.Add("ORDER: with no crew picked the card says nothing useful.");

            // Men elsewhere: every order can be given - the approach CARRIES the demand
            // or the threat to the door (the RACKUI-001 chain: ApproachBusinessCommand
            // takes the intent and the arrival resolves it), so from range the rows
            // read available with the walking note rather than faded.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, true, true, false, rows);
            if (rows.Count != 3)
                failures.Add("ORDER: the rows vanish instead of explaining themselves.");
            else
            {
                if (!Offers(rows, TerritoryRacketIntent.Approach, true))
                    failures.Add("ORDER: the men could not be sent to the door.");
                if (!Offers(rows, TerritoryRacketIntent.Demand, true) ||
                    !Offers(rows, TerritoryRacketIntent.Threaten, true))
                    failures.Add(
                        "ORDER: a demand from range no longer walks to the door.");
            }

            // Men at the door: the conversations open, and walking up again does not.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Defiant, true, true, true, rows);
            if (!Offers(rows, TerritoryRacketIntent.Demand, true) ||
                !Offers(rows, TerritoryRacketIntent.Threaten, true))
                failures.Add("ORDER: with the men at the door there is nothing to say.");
            if (Offers(rows, TerritoryRacketIntent.Approach, true))
                failures.Add("ORDER: the men were sent to a door they are standing at.");

            // A shop already paying us is COLLECTED from, never asked again (ECON-008):
            // the round is its one live row, and the demand never lights.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Compliant, true, true, true, rows);
            if (!Offers(rows, TerritoryRacketIntent.Collect, true))
                failures.Add("ORDER: a paying shop's card does not offer the round.");
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Available && rows[i].Intent != TerritoryRacketIntent.Collect)
                    failures.Add("ORDER: a shop that already pays us was asked again.");

            // Every intent is named exactly once, whatever the situation.
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Hesitant, true, true, true, rows);
            var seen = new List<TerritoryRacketIntent>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (seen.Contains(rows[i].Intent))
                    failures.Add("ORDER: the same intent is on the card twice.");
                seen.Add(rows[i].Intent);
                if (string.IsNullOrEmpty(rows[i].Label))
                    failures.Add("ORDER: a row has no words on it.");
            }
        }

        static bool Offers(
            List<TerritoryRacketOrder> rows, TerritoryRacketIntent intent, bool available)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Intent == intent && rows[i].Available == available)
                    return true;
            return false;
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
