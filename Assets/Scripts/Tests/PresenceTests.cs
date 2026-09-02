using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-79 / PRES-001 through PRES-010: who is really standing
    /// on a block, what that is worth, what the block remembers of it and how it fades.
    /// Nothing here touches a GameObject - the ledger is pure and the scheduler drives it
    /// in game hours, so the whole epic is testable with the editor idle.
    /// </summary>
    public static class PresenceTests
    {
        const float Minute = 1f / 60f;

        static readonly TerritoryBlockId BlockA =
            new TerritoryBlockId("core:1987:1:2:3:4:5:res");
        static readonly TerritoryBlockId BlockB =
            new TerritoryBlockId("core:1987:1:2:3:9:9:res");

        public static List<string> Run()
        {
            var failures = new List<string>();

            PresenceIsQueryablePerGangAndBlock(failures);
            OnlyBodiesOnTheGroundCount(failures);
            RankIsWeightedAndResponsibilityIsNot(failures);
            EveryCharacterCountsExactlyOnce(failures);
            DrivingThroughIsWorthLessThanStandingThere(failures);
            TheBlockRemembersRecentWork(failures);
            WhatTheBlockRemembersFades(failures);
            RivalsRunOnTheSameArithmetic(failures);
            ThePlayerReadsWordsAndNeverRivalNumbers(failures);
            TheBreakdownReconcilesWithTheTotal(failures);
            PresenceAndTheDeedsDoNotWipeEachOther(failures);
            AHouseWithNoBodiesHoldsTheSameGround(failures);

            return failures;
        }

        // ------------------------------------------------------------------ RIVAL-008

        /// <summary>
        /// A FAMILY THE CITY NEVER STOOD UP HOLDS THE SAME GROUND. The paper sample
        /// contributes one observation per man of a posted crew, at the activity a man
        /// standing on a corner has - the SAME weights the street's own bodies are
        /// counted with. There is one presence rule, and the paper clock is not a
        /// second one.
        /// </summary>
        static void AHouseWithNoBodiesHoldsTheSameGround(List<string> failures)
        {
            var config = Config();

            // The same crew, twice: once as bodies the street walked over, once as the
            // synthetic observations a paper house's posting builds.
            var street = new TerritoryPresenceLedger(config);
            Sample(street, Minute,
                Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                Man(0, 2, block: BlockA),
                Man(0, 3, block: BlockA));

            var paper = new TerritoryPresenceLedger(config);
            Sample(paper, Minute,
                Man(7, 11, block: BlockB, rank: TerritoryRank.Lieutenant),
                Man(7, 12, block: BlockB),
                Man(7, 13, block: BlockB));

            if (Off(street.TotalOf(BlockA, Gang(0)), paper.TotalOf(BlockB, Gang(7))))
                failures.Add("RIVAL-008: a crew on paper is worth " +
                             paper.TotalOf(BlockB, Gang(7)) + " where the same crew on " +
                             "the street is worth " + street.TotalOf(BlockA, Gang(0)) + ".");

            // And it is remembered and forgotten by the same clock.
            for (var i = 0; i < 20; i++)
            {
                Sample(street, Minute, Man(0, 1, block: BlockA));
                Sample(paper, Minute, Man(7, 11, block: BlockB));
            }
            street.DecayResidual(3.0);
            paper.DecayResidual(3.0);
            if (Off(street.ResidualOf(BlockA, Gang(0)), paper.ResidualOf(BlockB, Gang(7))))
                failures.Add("RIVAL-008: the street and the paper remember differently.");
        }

        // ------------------------------------------------------------------- PRES-001

        /// <summary>
        /// Presence is exact, keyed by family AND block, and several families can have it
        /// on the same block at once. It never says who holds the block: that stays a
        /// reading of the deeds, and a block full of our men with no premises on it is
        /// still not ours.
        /// </summary>
        static void PresenceIsQueryablePerGangAndBlock(List<string> failures)
        {
            var ledger = new TerritoryPresenceLedger(Config());

            Sample(ledger, Minute,
                Man(0, 1, block: BlockA),
                Man(0, 2, block: BlockA),
                Man(3, 7, block: BlockA),
                Man(0, 4, block: BlockB));

            if (Off(ledger.PhysicalOf(BlockA, Gang(0)), 20f))
                failures.Add("PRES-001: two of our men on a block are not worth two men.");
            if (Off(ledger.PhysicalOf(BlockA, Gang(3)), 10f))
                failures.Add("PRES-001: a second family on the same block has no Presence of its own.");
            if (Off(ledger.PhysicalOf(BlockB, Gang(0)), 10f))
                failures.Add("PRES-001: Presence is not keyed by block.");
            if (ledger.TotalOf(BlockB, Gang(3)) != 0f)
                failures.Add("PRES-001: a family with nobody on the block still has Presence there.");

            var signals = ledger.Signals(BlockA, TerritoryBlockSignals.Empty, new List<TerritoryGangSignals>());
            if (signals.Control != TerritoryControlState.Unknown || signals.LeadingGangId.IsValid)
                failures.Add("PRES-001: writing Presence assigned control of the block.");
            if (signals.Gangs.Count != 2)
                failures.Add("PRES-001: the published signals do not carry both families.");
        }

        // ------------------------------------------------------------------- PRES-002

        /// <summary>
        /// Only real bodies on the ground count. A man who moves off the block, or who
        /// stops being sampled at all because he is dead, stops contributing on the very
        /// next sample - and a man merely assigned to a lieutenant contributes nothing,
        /// because the ledger is never told about assignments in the first place.
        /// </summary>
        static void OnlyBodiesOnTheGroundCount(List<string> failures)
        {
            var ledger = new TerritoryPresenceLedger(Config(residualDepositPerHour: 0f));

            Sample(ledger, Minute, Man(0, 1, block: BlockA), Man(0, 2, block: BlockA));
            if (Off(ledger.PhysicalOf(BlockA, Gang(0)), 20f))
                failures.Add("PRES-002: two men standing on the block are not counted.");

            // One walks to the next block: the ground he left drops him at once.
            Sample(ledger, Minute, Man(0, 1, block: BlockA), Man(0, 2, block: BlockB));
            if (Off(ledger.PhysicalOf(BlockA, Gang(0)), 10f) ||
                Off(ledger.PhysicalOf(BlockB, Gang(0)), 10f))
                failures.Add("PRES-002: moving a man between blocks did not move his contribution.");

            // The other is killed: he is simply not sampled again.
            Sample(ledger, Minute, Man(0, 2, block: BlockB));
            if (ledger.PhysicalOf(BlockA, Gang(0)) != 0f)
                failures.Add("PRES-002: a man who is gone still holds ground.");

            // Nobody left anywhere: with nothing remembered, Presence is zero.
            Sample(ledger, Minute);
            if (ledger.TotalOf(BlockB, Gang(0)) != 0f)
                failures.Add("PRES-002: an empty block still reports Presence.");
        }

        // ------------------------------------------------------------------- PRES-003

        /// <summary>
        /// A lieutenant standing on the corner is worth more than a hood standing on it,
        /// and the boss more again. Take the man off the street and the weight goes with
        /// him, however the block's paperwork reads - responsibility is not Presence.
        /// </summary>
        static void RankIsWeightedAndResponsibilityIsNot(List<string> failures)
        {
            var config = Config();
            var hoods = new TerritoryPresenceLedger(config);
            var withLieutenant = new TerritoryPresenceLedger(config);
            var withBoss = new TerritoryPresenceLedger(config);

            Sample(hoods, Minute, Man(0, 1, block: BlockA), Man(0, 2, block: BlockA));
            Sample(withLieutenant, Minute,
                Man(0, 1, block: BlockA),
                Man(0, 2, block: BlockA, rank: TerritoryRank.Lieutenant));
            Sample(withBoss, Minute,
                Man(0, 1, block: BlockA),
                Man(0, 2, block: BlockA, rank: TerritoryRank.Boss));

            var plain = hoods.PhysicalOf(BlockA, Gang(0));
            var led = withLieutenant.PhysicalOf(BlockA, Gang(0));
            var bossed = withBoss.PhysicalOf(BlockA, Gang(0));
            if (!(led > plain) || !(bossed > led))
                failures.Add("PRES-003: rank on the ground is worth nothing.");
            if (Off(led - plain, config.PointsPerContributor *
                    (config.LieutenantWeight - config.HoodWeight)))
                failures.Add("PRES-003: the lieutenant's weight is not the configured one.");

            // The lieutenant walks away; only the hood is sampled. The block may still be
            // his to answer for - the ledger has never been told and must not care.
            Sample(withLieutenant, Minute, Man(0, 1, block: BlockA));
            if (Off(withLieutenant.PhysicalOf(BlockA, Gang(0)), plain / 2f))
                failures.Add("PRES-003: removing the lieutenant's body did not remove his weight.");
        }

        // ------------------------------------------------------------------- PRES-004

        /// <summary>
        /// The dedupe unit is the character. A four-man crew is four contributions however
        /// many ways it is looked at, its leader is one body and not a multiplier, and
        /// splitting the same men over two blocks conserves what they are worth.
        /// </summary>
        static void EveryCharacterCountsExactlyOnce(List<string> failures)
        {
            var ledger = new TerritoryPresenceLedger(Config());

            // The same four men, seen once individually and once through the crew's own
            // projection of its leader.
            Sample(ledger, Minute,
                Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                Man(0, 2, block: BlockA),
                Man(0, 3, block: BlockA),
                Man(0, 4, block: BlockA),
                Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                Man(0, 3, block: BlockA));

            var contributors = new List<TerritoryPresenceContributor>();
            ledger.CollectContributors(BlockA, Gang(0), contributors);
            if (contributors.Count != 4)
                failures.Add("PRES-004: the same man was counted twice on one block.");

            var whole = ledger.PhysicalOf(BlockA, Gang(0));
            if (Off(whole, 50f))
                failures.Add("PRES-004: a four-man crew with a lieutenant is not worth its parts.");

            Sample(ledger, Minute,
                Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                Man(0, 2, block: BlockA),
                Man(0, 3, block: BlockB),
                Man(0, 4, block: BlockB));
            var split = ledger.PhysicalOf(BlockA, Gang(0)) + ledger.PhysicalOf(BlockB, Gang(0));
            if (Off(split, whole))
                failures.Add("PRES-004: splitting the crew over two blocks changed what it is worth.");
        }

        // ------------------------------------------------------------------- PRES-005

        /// <summary>A crew driving through a block is not holding it.</summary>
        static void DrivingThroughIsWorthLessThanStandingThere(List<string> failures)
        {
            var config = Config();
            var driving = new TerritoryPresenceLedger(config);
            var walking = new TerritoryPresenceLedger(config);
            var posted = new TerritoryPresenceLedger(config);

            Sample(driving, Minute,
                Man(0, 1, block: BlockA, activity: TerritoryActorActivity.Transit),
                Man(0, 2, block: BlockA, activity: TerritoryActorActivity.Transit));
            Sample(walking, Minute,
                Man(0, 1, block: BlockA, activity: TerritoryActorActivity.Moving),
                Man(0, 2, block: BlockA, activity: TerritoryActorActivity.Moving));
            Sample(posted, Minute,
                Man(0, 1, block: BlockA, activity: TerritoryActorActivity.Stationed),
                Man(0, 2, block: BlockA, activity: TerritoryActorActivity.Stationed));

            var through = driving.PhysicalOf(BlockA, Gang(0));
            var afoot = walking.PhysicalOf(BlockA, Gang(0));
            var stood = posted.PhysicalOf(BlockA, Gang(0));
            if (!(through < afoot) || !(afoot < stood))
                failures.Add("PRES-005: what the men were doing made no difference to Presence.");
            if (!(through < stood / 2f))
                failures.Add("PRES-005: a crew driving through is not materially less than one posted.");

            var loud = new TerritoryPresenceLedger(Config(transitWeight: 1f));
            Sample(loud, Minute,
                Man(0, 1, block: BlockA, activity: TerritoryActorActivity.Transit),
                Man(0, 2, block: BlockA, activity: TerritoryActorActivity.Transit));
            if (Off(loud.PhysicalOf(BlockA, Gang(0)), stood))
                failures.Add("PRES-005: the activity weights are not configurable.");
        }

        // ------------------------------------------------------------------- PRES-006

        /// <summary>
        /// Leaving a street does not wipe you off it. One brief pass leaves a little,
        /// working it for an hour leaves more, and what is remembered is capped so no
        /// amount of standing about makes a block permanently yours.
        /// </summary>
        static void TheBlockRemembersRecentWork(List<string> failures)
        {
            var brief = new TerritoryPresenceLedger(Config());
            Sample(brief, Minute, Man(0, 1, block: BlockA));
            Sample(brief, Minute);

            var afterOnePass = brief.TotalOf(BlockA, Gang(0));
            if (!(afterOnePass > 0f))
                failures.Add("PRES-006: leaving a block made Presence instantly zero.");
            if (!(afterOnePass < 1f))
                failures.Add("PRES-006: one brief pass left more than a trace of memory.");
            if (brief.PhysicalOf(BlockA, Gang(0)) != 0f)
                failures.Add("PRES-006: memory was mistaken for a body on the ground.");

            var worked = new TerritoryPresenceLedger(Config());
            for (var i = 0; i < 60; i++)
                Sample(worked, Minute, Man(0, 1, block: BlockA));
            Sample(worked, Minute);
            var afterAnHour = worked.TotalOf(BlockA, Gang(0));
            if (!(afterAnHour > afterOnePass * 10f))
                failures.Add("PRES-006: an hour of work is remembered no better than one pass.");

            var config = Config();
            var forever = new TerritoryPresenceLedger(config);
            for (var i = 0; i < 60 * 24; i++)
                Sample(forever, Minute, Man(0, 1, block: BlockA));
            if (forever.ResidualOf(BlockA, Gang(0)) > config.ResidualCap + 0.001f)
                failures.Add("PRES-006: what a block remembers is not capped.");
        }

        // ------------------------------------------------------------------- PRES-007

        /// <summary>
        /// Memory fades on the scheduler's game hours, so it is the same fade at 10 fps
        /// and 200, and a paused clock fades nothing. What is standing there is measured
        /// again every sample and has nothing to decay.
        /// </summary>
        static void WhatTheBlockRemembersFades(List<string> failures)
        {
            var config = Config();
            var ledger = Seeded(config, out var seeded);

            ledger.DecayResidual(0.0);
            if (Off(ledger.ResidualOf(BlockA, Gang(0)), seeded))
                failures.Add("PRES-007: no game time passed and the memory faded anyway.");

            ledger.DecayResidual(config.ResidualHalfLifeHours);
            if (Off(ledger.ResidualOf(BlockA, Gang(0)), seeded / 2f, 0.01f))
                failures.Add("PRES-007: one half-life did not halve what the block remembers.");

            // The same hours in many small steps must land in the same place.
            var stepped = Seeded(config, out _);
            for (var i = 0; i < 24; i++)
                stepped.DecayResidual(config.ResidualHalfLifeHours / 24.0);
            if (Off(stepped.ResidualOf(BlockA, Gang(0)),
                    ledger.ResidualOf(BlockA, Gang(0)), 0.01f))
                failures.Add("PRES-007: the fade depends on how the hours were cut up.");

            for (var i = 0; i < 120; i++)
                ledger.DecayResidual(1.0);
            if (ledger.TotalOf(BlockA, Gang(0)) > 0.05f)
                failures.Add("PRES-007: sustained absence does not trend Presence to nothing.");

            // A man still standing there is untouched by any amount of decay.
            var held = new TerritoryPresenceLedger(config);
            Sample(held, Minute, Man(0, 1, block: BlockA));
            var physical = held.PhysicalOf(BlockA, Gang(0));
            held.DecayResidual(48.0);
            if (Off(held.PhysicalOf(BlockA, Gang(0)), physical))
                failures.Add("PRES-007: decay ate the men who are actually standing there.");
        }

        // ------------------------------------------------------------------- PRES-008

        /// <summary>
        /// A rival's men are counted by exactly the same arithmetic as ours. Mirror the
        /// bodies and the numbers mirror; a block only a rival works shows only his.
        /// </summary>
        static void RivalsRunOnTheSameArithmetic(List<string> failures)
        {
            var config = Config();
            var ledger = new TerritoryPresenceLedger(config);

            Sample(ledger, Minute,
                Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                Man(0, 2, block: BlockA),
                Man(0, 3, block: BlockA, activity: TerritoryActorActivity.Transit),
                Man(7, 11, block: BlockB, rank: TerritoryRank.Lieutenant),
                Man(7, 12, block: BlockB),
                Man(7, 13, block: BlockB, activity: TerritoryActorActivity.Transit));

            if (Off(ledger.TotalOf(BlockA, Gang(0)), ledger.TotalOf(BlockB, Gang(7))))
                failures.Add("PRES-008: mirrored men in two families are not worth the same.");
            if (ledger.TotalOf(BlockB, Gang(0)) != 0f)
                failures.Add("PRES-008: we have Presence on a block we have never set foot on.");
            if (!(ledger.TotalOf(BlockB, Gang(7)) > 0f))
                failures.Add("PRES-008: a rival working a block alone has no Presence there.");

            // Memory and decay are the same for both, too.
            for (var i = 0; i < 30; i++)
                Sample(ledger, Minute,
                    Man(0, 1, block: BlockA), Man(7, 11, block: BlockB));
            ledger.DecayResidual(2.0);
            if (Off(ledger.ResidualOf(BlockA, Gang(0)), ledger.ResidualOf(BlockB, Gang(7))))
                failures.Add("PRES-008: the two families do not remember and forget alike.");
        }

        // ------------------------------------------------------------------- PRES-009

        /// <summary>
        /// The player reads words, never the numbers. Every band of the scale has its own
        /// word up to Dominant, and a rival the player is not allowed to see reads Unknown
        /// rather than his true strength - while the debug truth still holds the float.
        /// </summary>
        static void ThePlayerReadsWordsAndNeverRivalNumbers(List<string> failures)
        {
            var scale = TerritoryPresentationProfile.Default.Presence;
            if (scale.Describe(null) != scale.UnknownLabel ||
                scale.Describe(0f) != scale.NoneLabel ||
                scale.Describe(scale.WeakAt) != scale.WeakLabel ||
                scale.Describe(scale.ModerateAt) != scale.ModerateLabel ||
                scale.Describe(scale.StrongAt) != scale.StrongLabel ||
                scale.Describe(scale.DominantAt) != scale.DominantLabel)
                failures.Add("PRES-009: the Presence bands do not map to their words.");
            if (scale.Describe(scale.ModerateAt - 0.001f) != scale.WeakLabel ||
                scale.Describe(scale.DominantAt - 0.001f) != scale.StrongLabel)
                failures.Add("PRES-009: a threshold edge fell into the wrong band.");

            var state = new TerritorySimulationState(new[] { Definition() });
            var truth = new TerritoryTruthQuery(state);
            state.SetSignals(BlockA, new TerritoryBlockSignals(
                control: TerritoryControlState.Contested,
                gangs: new[]
                {
                    new TerritoryGangSignals(Gang(0), 90f, 40f),
                    new TerritoryGangSignals(Gang(7), 70f, 60f),
                }));

            var open = new TerritoryPlayerQuery(
                truth, Gang(0), TerritoryPresentationProfile.Default);
            if (!open.TryGetBlock(BlockA, out var seen) ||
                seen.Presence != TerritoryPresentationProfile.Default.Presence.DominantLabel ||
                seen.RivalPresence != TerritoryPresentationProfile.Default.Presence.StrongLabel)
                failures.Add("PRES-009: the player page does not describe Presence in words.");

            var blind = new TerritoryPlayerQuery(
                truth, Gang(0), TerritoryPresentationProfile.Default, new OwnHouseOnly());
            if (!blind.TryGetBlock(BlockA, out var filtered) ||
                filtered.RivalPresence !=
                    TerritoryPresentationProfile.Default.Presence.UnknownLabel)
                failures.Add("PRES-009: a rival the player cannot see still reports his strength.");
            if (filtered.Presence != seen.Presence)
                failures.Add("PRES-009: the knowledge filter hid the player's own Presence.");

            if (!truth.TryGetBlock(BlockA, out var exact) ||
                !exact.Signals.TryGetGang(Gang(7), out var rival) ||
                Off(rival.Presence, 70f))
                failures.Add("PRES-009: the exact rival value is no longer in the debug truth.");
        }

        // ------------------------------------------------------------------- PRES-010

        /// <summary>
        /// The inspector's parts must add up to its total: every contributor, its rank and
        /// activity weighting, plus what the block remembers, is the published Presence.
        /// </summary>
        static void TheBreakdownReconcilesWithTheTotal(List<string> failures)
        {
            var config = Config();
            var ledger = new TerritoryPresenceLedger(config);
            for (var i = 0; i < 10; i++)
                Sample(ledger, Minute,
                    Man(0, 1, block: BlockA, rank: TerritoryRank.Lieutenant),
                    Man(0, 2, block: BlockA),
                    Man(0, 3, block: BlockA, activity: TerritoryActorActivity.Transit));

            var contributors = new List<TerritoryPresenceContributor>();
            ledger.CollectContributors(BlockA, Gang(0), contributors);
            var summed = 0f;
            for (var i = 0; i < contributors.Count; i++)
            {
                var contributor = contributors[i];
                if (Off(contributor.Contribution,
                        config.ContributionOf(contributor.Rank, contributor.Activity)))
                    failures.Add("PRES-010: a contributor's weight is not the configured one.");
                summed += contributor.Contribution;
            }

            if (Off(summed, ledger.PhysicalOf(BlockA, Gang(0))))
                failures.Add("PRES-010: the listed contributors do not add up to the physical total.");
            if (Off(ledger.PhysicalOf(BlockA, Gang(0)) + ledger.ResidualOf(BlockA, Gang(0)),
                    ledger.TotalOf(BlockA, Gang(0))))
                failures.Add("PRES-010: physical plus remembered is not the published total.");

            var gangs = new List<TerritoryGangPresence>();
            ledger.CollectGangs(BlockA, gangs);
            if (gangs.Count != 1 || Off(gangs[0].Total, ledger.TotalOf(BlockA, Gang(0))))
                failures.Add("PRES-010: the per-family breakdown disagrees with the query.");
        }

        // ------------------------------------------------------------ the shared store

        /// <summary>
        /// Presence and the deeds live in the same per-family record and have different
        /// owners, so each pass must hand the other's number back untouched - otherwise
        /// the quarter-hour control sweep would quietly wipe the men off every block.
        /// </summary>
        static void PresenceAndTheDeedsDoNotWipeEachOther(List<string> failures)
        {
            var ledger = new TerritoryPresenceLedger(Config());
            Sample(ledger, Minute, Man(0, 1, block: BlockA), Man(7, 2, block: BlockA));

            var scratch = new List<TerritoryGangSignals>();
            var withPresence = ledger.Signals(BlockA, TerritoryBlockSignals.Empty, scratch);

            var deeds = new TerritoryControlDerivation.Tally();
            deeds.Add(7);
            deeds.Add(7);
            var afterControl = TerritoryControlDerivation.Signals(deeds, withPresence, scratch);

            var standing = ledger.TotalOf(BlockA, Gang(0));
            if (!afterControl.TryGetGang(Gang(0), out var ours) || Off(ours.Presence, standing))
                failures.Add("Store: the control pass wiped our men off the block.");
            if (!afterControl.TryGetGang(Gang(7), out var theirs) ||
                Off(theirs.Presence, standing) || Off(theirs.Influence, 100f))
                failures.Add("Store: a family's deeds and its men do not survive together.");
            if (afterControl.Control != TerritoryControlState.Controlled ||
                afterControl.LeadingGangId != Gang(7))
                failures.Add("Store: the control reading was lost in the merge.");

            // And back the other way: writing Presence must not touch the deeds.
            Sample(ledger, Minute, Man(0, 1, block: BlockA));
            var afterPresence = ledger.Signals(BlockA, afterControl, scratch);
            if (!afterPresence.TryGetGang(Gang(7), out var stillTheirs) ||
                Off(stillTheirs.Influence, 100f))
                failures.Add("Store: the Presence pass wiped the deeds on the block.");
            if (afterPresence.Control != TerritoryControlState.Controlled)
                failures.Add("Store: the Presence pass changed who holds the block.");
            if (stillTheirs.Presence <= 0f || stillTheirs.Presence >= standing)
                failures.Add("Store: a family that left the block is not fading out of it.");

            // Two writes of the same situation must be the same signals, or the whole
            // city would be rewritten on every tick.
            var repeat = ledger.Signals(BlockA, afterPresence, scratch);
            if (!TerritoryControlDerivation.Same(afterPresence, repeat))
                failures.Add("Store: an unchanged block would be rewritten every tick.");
        }

        // ------------------------------------------------------------------- fixtures

        static TerritoryPresenceConfig Config(
            float transitWeight = 0.2f, float residualDepositPerHour = 0.5f) =>
            new TerritoryPresenceConfig(
                transitWeight: transitWeight,
                residualDepositPerHour: residualDepositPerHour);

        static TerritoryPresenceLedger Seeded(TerritoryPresenceConfig config, out float residual)
        {
            var ledger = new TerritoryPresenceLedger(config);
            for (var i = 0; i < 120; i++)
                Sample(ledger, Minute, Man(0, 1, block: BlockA));
            Sample(ledger, Minute);
            residual = ledger.ResidualOf(BlockA, Gang(0));
            return ledger;
        }

        static TerritoryGangId Gang(int id) => new TerritoryGangId(id);

        readonly struct Body
        {
            public Body(TerritoryBlockId blockId, TerritoryActorObservation actor)
            {
                BlockId = blockId;
                Actor = actor;
            }

            public TerritoryBlockId BlockId { get; }
            public TerritoryActorObservation Actor { get; }
        }

        static Body Man(
            int gang,
            int character,
            TerritoryBlockId block = default,
            TerritoryRank rank = TerritoryRank.Hood,
            TerritoryActorActivity activity = TerritoryActorActivity.Stationed,
            int crew = 1) =>
            new Body(block, new TerritoryActorObservation(
                new TerritoryCharacterId(character),
                new TerritoryGangId(gang),
                TerritoryCommandNodeId.Crew(crew),
                "man " + character,
                "gang " + gang,
                rank == TerritoryRank.Lieutenant,
                rank,
                activity));

        /// <summary>One PhysicalPresence tick with exactly these bodies on the street.</summary>
        static void Sample(TerritoryPresenceLedger ledger, float cadenceHours, params Body[] bodies)
        {
            ledger.BeginSample();
            for (var i = 0; bodies != null && i < bodies.Length; i++)
                ledger.Contribute(bodies[i].BlockId, bodies[i].Actor);
            ledger.CommitSample(cadenceHours);
        }

        static TerritoryBlockDefinition Definition() =>
            new TerritoryBlockDefinition(
                BlockA,
                12,
                TerritoryIdentity.CoreNeighborhood(1987, 1),
                "Downtown",
                "Downtown Block 01",
                new TerritoryBounds(10f, 20f, 80f, 60f),
                "CoreTerritoryPlan.StableId");

        static bool Off(float value, float expected, float tolerance = 0.001f) =>
            Math.Abs(value - expected) > tolerance;

        /// <summary>A knowledge filter that lets the player see only his own house.</summary>
        sealed class OwnHouseOnly : ITerritoryKnowledgeFilter
        {
            public TerritoryBlockSignals Observe(
                TerritoryBlockTruth truth, TerritoryGangId viewingGangId)
            {
                var signals = truth?.Signals ?? TerritoryBlockSignals.Empty;
                var kept = new List<TerritoryGangSignals>();
                for (var i = 0; i < signals.Gangs.Count; i++)
                    if (signals.Gangs[i].GangId == viewingGangId)
                        kept.Add(signals.Gangs[i]);

                return new TerritoryBlockSignals(
                    signals.LocalFear,
                    signals.BusinessCompliance,
                    signals.CompliantBusinesses,
                    signals.TotalBusinesses,
                    signals.Control,
                    signals.LeadingGangId,
                    kept);
            }
        }
    }
}
