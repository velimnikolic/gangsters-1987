using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-120 / CTRL-001 through CTRL-016: what a street is worth
    /// to each family, what that makes it, how it changes hands, and what it costs a house
    /// not to answer for the ground it is paid to protect. Every bit of it is derived, so
    /// none of it needs a scene.
    /// </summary>
    public static class ControlTests
    {
        static readonly TerritoryBlockId BlockA =
            new TerritoryBlockId("core:1987:1:2:3:4:5:res");
        static readonly TerritoryBlockId BlockB =
            new TerritoryBlockId("core:1987:1:2:3:9:9:res");

        public static List<string> Run()
        {
            var failures = new List<string>();

            EveryInputIsWeighedAndNoneIsOwned(failures);
            AStreetIsReadNotSet(failures);
            TwoHousesCloseTogetherIsAFight(failures);
            AStreetDoesNotChangeItsMindOnOneReading(failures);
            GroundIsWonAndLostByItsInputsAlone(failures);
            AHouseThatNeverAnswersIsWorthLess(failures);
            AQuarterIsWhatItsStreetsAddUpTo(failures);

            return failures;
        }

        // ------------------------------------------------------------------- CTRL-001/006

        /// <summary>
        /// Four inputs, each weighted, each independently visible in the breakdown, and
        /// the same state always gives the same number.
        /// </summary>
        static void EveryInputIsWeighedAndNoneIsOwned(List<string> failures)
        {
            var config = TerritoryControlConfig.Default;
            var score = config.Score(new TerritoryControlInputs(Gang(0), 60f, 40f, 50f, 1f));

            if (Off(score.PresenceTerm, config.PresenceWeight * 60f) ||
                Off(score.FearTerm, config.FearWeight * 40f) ||
                Off(score.ComplianceTerm, config.ComplianceWeight * 50f))
                failures.Add("CTRL-001: an input is not weighed by its own weight.");
            if (Off(score.Total,
                    score.PresenceTerm + score.FearTerm + score.ComplianceTerm))
                failures.Add("CTRL-006: the total is not what its terms add up to.");

            var again = config.Score(new TerritoryControlInputs(Gang(0), 60f, 40f, 50f, 1f));
            if (Off(score.Total, again.Total))
                failures.Add("CTRL-006: the same state gave two different scores.");

            // Retune one weight and only that term moves.
            var quiet = new TerritoryControlConfig(fearWeight: 0f);
            var unfeared = quiet.Score(new TerritoryControlInputs(Gang(0), 60f, 40f, 50f, 1f));
            if (unfeared.FearTerm != 0f || Off(unfeared.PresenceTerm, score.PresenceTerm))
                failures.Add("CTRL-006: the weights are not what the score reads.");

            // Power scales the lot, and never divides by nothing.
            var weak = config.Score(new TerritoryControlInputs(Gang(0), 60f, 40f, 50f, 0.5f));
            if (Off(weak.Total, score.Total * 0.5f))
                failures.Add("CTRL-016: Power does not scale what a family is worth.");
            var unknown = config.Score(new TerritoryControlInputs(Gang(0), 60f, 40f, 50f, 0f));
            if (Off(unknown.Total, score.Total))
                failures.Add("CTRL-016: a family with no record is punished for having none.");
        }

        // ------------------------------------------------------------------- CTRL-007

        /// <summary>
        /// The ladder: nobody's street, influence, held, held outright - decided by what
        /// the leading family is worth, and by nothing that was ever written down as an
        /// owner.
        /// </summary>
        static void AStreetIsReadNotSet(List<string> failures)
        {
            var config = TerritoryControlConfig.Default;

            if (Read(config, 0f) != TerritoryControlState.Uncontrolled)
                failures.Add("CTRL-007: an empty street is not neutral.");
            if (Read(config, config.InfluencedAt) != TerritoryControlState.Influenced)
                failures.Add("CTRL-007: a family with a foothold does not read as influence.");
            if (Read(config, config.ControlledAt) != TerritoryControlState.Controlled)
                failures.Add("CTRL-007: a family over the line does not hold the street.");
            if (Read(config, config.DominatedAt) != TerritoryControlState.Dominated)
                failures.Add("CTRL-007: a street held outright is not called that.");
            if (Read(config, config.ControlledAt - 0.01f) != TerritoryControlState.Influenced)
                failures.Add("CTRL-007: a threshold edge fell into the wrong band.");

            // No single pillar holds a street on its own (CTRL-003, CTRL-004): all the men
            // in the city, or a street that only remembers one killing, is not control.
            var presenceOnly = config.Score(
                new TerritoryControlInputs(Gang(0), 100f, 0f, 0f, 1f));
            if (TerritoryControlReading.Read(
                    Only(presenceOnly), config, false, out _, out _, out _) >
                TerritoryControlState.Influenced)
                failures.Add("CTRL-003: men alone took a street.");

            var fearOnly = config.Score(new TerritoryControlInputs(Gang(0), 0f, 100f, 0f, 1f));
            if (TerritoryControlReading.Read(
                    Only(fearOnly), config, false, out _, out _, out _) >
                TerritoryControlState.Influenced)
                failures.Add("CTRL-004: fear alone took a street.");

            // Two pillars together do what neither does alone.
            var both = config.Score(new TerritoryControlInputs(Gang(0), 100f, 100f, 0f, 1f));
            if (TerritoryControlReading.Read(
                    Only(both), config, false, out _, out _, out _) <
                TerritoryControlState.Controlled)
                failures.Add("CTRL-006: men and fear together still hold nothing.");

            // And the words exist for the player.
            var words = TerritoryPresentationProfile.Default;
            if (string.IsNullOrEmpty(words.Dominated) || words.Dominated == words.Controlled)
                failures.Add("CTRL-007: the player has no word for a street held outright.");
        }

        // ------------------------------------------------------------------- CTRL-008

        /// <summary>
        /// Two houses worth something, close together, is a fight - no war has to be
        /// declared. Two houses worth nothing is not a fight, it is an empty street. And
        /// the gap that ends one is wider than the gap that starts it.
        /// </summary>
        static void TwoHousesCloseTogetherIsAFight(List<string> failures)
        {
            var config = TerritoryControlConfig.Default;

            var strong = Scores((0, 50f), (7, 45f));
            if (TerritoryControlReading.Read(strong, config, false, out _, out _, out _) !=
                TerritoryControlState.Contested)
                failures.Add("CTRL-008: two strong houses on one street is not contested.");

            var feeble = Scores((0, 8f), (7, 7f));
            if (TerritoryControlReading.Read(feeble, config, false, out _, out _, out _) ==
                TerritoryControlState.Contested)
                failures.Add("CTRL-008: two nobodies squabbling made a contested block.");

            var apart = Scores((0, 60f), (7, 20f));
            if (TerritoryControlReading.Read(apart, config, false, out var leader, out _, out _) ==
                    TerritoryControlState.Contested || leader != Gang(0))
                failures.Add("CTRL-008: a street one house clearly holds reads as a fight.");

            // Hysteresis: a gap that would not have started a fight still does not end one.
            var drifting = Scores((0, 50f), (7, 37f));
            if (TerritoryControlReading.Read(drifting, config, false, out _, out _, out _) ==
                TerritoryControlState.Contested)
                failures.Add("CTRL-008: the entry margin is wider than configured.");
            if (TerritoryControlReading.Read(drifting, config, true, out _, out _, out _) !=
                TerritoryControlState.Contested)
                failures.Add("CTRL-008: a fight ends at the same gap that started it.");
        }

        // ------------------------------------------------------------------- CTRL-009

        /// <summary>
        /// A street does not change hands because of one reading. The same answer has to
        /// come back, twice running, before anything is announced - and then exactly once.
        /// </summary>
        static void AStreetDoesNotChangeItsMindOnOneReading(List<string> failures)
        {
            var ledger = new TerritoryControlLedger(new TerritoryControlConfig(holdTicks: 2));
            var held = Scores((0, 50f));

            if (ledger.Read(BlockA, held, 1.0, out _))
                failures.Add("CTRL-009: one reading was enough to change the street.");
            if (!ledger.Read(BlockA, held, 2.0, out var change))
                failures.Add("CTRL-009: two readings running changed nothing.");
            else if (change.Current != TerritoryControlState.Controlled ||
                     change.Leader != Gang(0))
                failures.Add("CTRL-009: the street changed to the wrong thing.");

            if (ledger.Read(BlockA, held, 3.0, out _))
                failures.Add("CTRL-009: an unchanged street was announced again.");

            // Flicker: one reading over the next line, then back. Nothing is announced.
            var higher = Scores((0, 80f));
            ledger.Read(BlockA, higher, 4.0, out _);
            if (ledger.Read(BlockA, held, 5.0, out _))
                failures.Add("CTRL-009: a street flickering at a line announced a change.");
            if (ledger.StateOf(BlockA) != TerritoryControlState.Controlled)
                failures.Add("CTRL-009: the street did not keep its footing through a wobble.");
        }

        // ------------------------------------------------------------------- CTRL-010

        /// <summary>
        /// Ground is lost the same way it is won - by the inputs moving. There is no
        /// command for either, and the walk down the ladder announces the loss.
        /// </summary>
        static void GroundIsWonAndLostByItsInputsAlone(List<string> failures)
        {
            var config = new TerritoryControlConfig(holdTicks: 1);
            var ledger = new TerritoryControlLedger(config);

            ledger.Read(BlockA, Scores((0, 80f)), 1.0, out _);
            if (ledger.StateOf(BlockA) != TerritoryControlState.Dominated)
                failures.Add("CTRL-009: the street was not taken outright.");

            // The men go home and the shops stop paying: the street walks back down.
            var seen = new List<TerritoryControlState>();
            var lost = false;
            foreach (var worth in new[] { 40f, 20f, 4f })
            {
                if (!ledger.Read(BlockA, Scores((0, worth)), 2.0, out var change))
                    continue;
                seen.Add(change.Current);
                lost |= change.LostControl;
            }

            if (seen.Count != 3 ||
                seen[0] != TerritoryControlState.Controlled ||
                seen[1] != TerritoryControlState.Influenced ||
                seen[2] != TerritoryControlState.Uncontrolled)
                failures.Add("CTRL-010: the street did not walk back down the ladder.");
            if (!lost)
                failures.Add("CTRL-010: a house losing a street it held was never announced.");
            if (ledger.LeaderOf(BlockA).IsValid)
                failures.Add("CTRL-010: an empty street still names a leader.");
        }

        // ------------------------------------------------------------------- CTRL-015

        /// <summary>
        /// A house that does not come when the shops it protects are hit is worth less on
        /// that street. Answering restores it, the street forgets in time, and an incident
        /// still inside its window is not yet a failure.
        /// </summary>
        static void AHouseThatNeverAnswersIsWorthLess(List<string> failures)
        {
            var config = TerritoryControlConfig.Default;
            var power = new TerritoryPowerLedger(config);

            if (Off(power.Coefficient(BlockA, Gang(0), 0), 1f))
                failures.Add("CTRL-015: a house nothing has happened to is already marked down.");

            power.Incident(BlockA, Gang(0), 0);
            if (Off(power.Coefficient(BlockA, Gang(0), 1.0), 1f))
                failures.Add("CTRL-015: a house was condemned before its window closed.");

            var after = config.PowerAnswerWindowHours + 1.0;
            if (!(power.Coefficient(BlockA, Gang(0), after) < 1f))
                failures.Add("CTRL-015: an unanswered incident cost the house nothing.");
            if (power.Coefficient(BlockA, Gang(0), after) < config.PowerFloor - 0.001f)
                failures.Add("CTRL-015: a house fell through the floor.");

            // Answering inside the window keeps the name clean.
            var answered = new TerritoryPowerLedger(config);
            answered.Incident(BlockB, Gang(0), 0);
            answered.Answered(BlockB, Gang(0), config.PowerAnswerWindowHours * 0.5);
            if (Off(answered.Coefficient(BlockB, Gang(0), after), 1f))
                failures.Add("CTRL-015: coming when called did not clear the name.");

            // And the street forgets.
            if (Off(power.Coefficient(BlockA, Gang(0), config.PowerMemoryHours + 1.0), 1f))
                failures.Add("CTRL-015: the street never forgets an old failure.");

            // Same history, same number, twice.
            var twice = power.Coefficient(BlockA, Gang(0), after);
            if (Off(twice, power.Coefficient(BlockA, Gang(0), after)))
                failures.Add("CTRL-015: the coefficient is not deterministic.");
        }

        // ------------------------------------------------------------------- CTRL-013

        /// <summary>
        /// A quarter is counted off its streets every time it is asked, and is not a thing
        /// anybody can take.
        /// </summary>
        static void AQuarterIsWhatItsStreetsAddUpTo(List<string> failures)
        {
            var ledger = new TerritoryControlLedger(new TerritoryControlConfig(holdTicks: 1));
            ledger.Read(BlockA, Scores((0, 80f)), 1.0, out _);
            ledger.Read(BlockB, Scores((7, 20f)), 1.0, out _);

            var hood = TerritoryIdentity.CoreNeighborhood(1987, 1);
            var status = TerritoryNeighborhoodReading.Read(
                hood, new List<TerritoryBlockId> { BlockA, BlockB }, ledger);

            if (status.Blocks != 2 || status.Dominated != 1 || status.Influenced != 1)
                failures.Add("CTRL-013: the quarter does not count its own streets.");
            if (status.Leader.IsValid)
                failures.Add("CTRL-013: a quarter split one street each named a leader.");

            // One street changes, and only its quarter's count moves.
            ledger.Read(BlockB, Scores((0, 80f)), 2.0, out _);
            var after = TerritoryNeighborhoodReading.Read(
                hood, new List<TerritoryBlockId> { BlockA, BlockB }, ledger);
            if (after.Dominated != 2 || after.Leader != Gang(0))
                failures.Add("CTRL-013: the quarter did not follow its streets.");

            var empty = TerritoryNeighborhoodReading.Read(
                hood, new List<TerritoryBlockId>(), ledger);
            if (empty.Blocks != 0 || empty.Leader.IsValid)
                failures.Add("CTRL-013: a quarter with no streets claims something.");
        }

        // ------------------------------------------------------------------- fixtures

        static TerritoryGangId Gang(int id) => new TerritoryGangId(id);

        static TerritoryControlState Read(TerritoryControlConfig config, float worth) =>
            TerritoryControlReading.Read(
                Scores((0, worth)), config, false, out _, out _, out _);

        static List<TerritoryControlScore> Only(TerritoryControlScore score) =>
            new List<TerritoryControlScore> { score };

        static List<TerritoryControlScore> Scores(params (int gang, float total)[] rows)
        {
            var scores = new List<TerritoryControlScore>();
            for (var i = 0; i < rows.Length; i++)
                scores.Add(new TerritoryControlScore(
                    Gang(rows[i].gang), rows[i].total, 0f, 0f, 1f, rows[i].total));
            return scores;
        }

        static bool Off(float value, float expected, float tolerance = 0.001f) =>
            Math.Abs(value - expected) > tolerance;
    }
}
