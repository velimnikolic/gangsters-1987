using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-90 / FEAR-001 through FEAR-013: what a street is afraid
    /// of, who it is afraid of, how long it remembers, what it costs to frighten it, and
    /// what the player is allowed to read. The ledger is pure and stamped in game hours,
    /// so all of it runs with the editor idle.
    /// </summary>
    public static class FearTests
    {
        static readonly TerritoryBlockId BlockA =
            new TerritoryBlockId("core:1987:1:2:3:4:5:res");
        static readonly TerritoryBlockId BlockB =
            new TerritoryBlockId("core:1987:1:2:3:9:9:res");
        static readonly TerritoryBusinessId Shop = new TerritoryBusinessId("biz:corner-shop");
        static readonly TerritoryBusinessId Bar = new TerritoryBusinessId("biz:bar");

        public static List<string> Run()
        {
            var failures = new List<string>();

            FearIsPerHousePerStreet(failures);
            AnActNobodyCanPinOnAHouseMakesNobodyFeared(failures);
            AnActInTheRoadBelongsToTheStreetBesideIt(failures);
            ViolenceIsWeighedByWhatItWasAndWhoSawIt(failures);
            AnIncidentAtAPremiseIsFeltByTheWholeBlock(failures);
            AStreetRemembersAfterTheMenAreGone(failures);
            WhatIsRememberedFades(failures);
            AnUnansweredRefusalCostsTheHouseItsName(failures);
            ViolenceBuysPoliceAttention(failures);
            ThePlayerReadsWordsAndATone(failures);
            FearRidesTheStoreWithoutWipingIt(failures);

            return failures;
        }

        // ------------------------------------------------------------------- FEAR-001

        /// <summary>
        /// Fear is per family per block. The same house is feared on one street and a
        /// rumour on the next, two houses can be feared on the same street at once, and
        /// what the block itself feels is the strongest of them.
        /// </summary>
        static void FearIsPerHousePerStreet(List<string> failures)
        {
            var ledger = new TerritoryFearLedger();

            ledger.Record(Act(0, BlockA, TerritoryFearCategory.Killing, hour: 0));
            ledger.Record(Act(7, BlockA, TerritoryFearCategory.Threat, hour: 0));
            ledger.Record(Act(0, BlockB, TerritoryFearCategory.Threat, hour: 0));

            var oursHere = ledger.FearOf(BlockA, Gang(0), 0);
            var theirsHere = ledger.FearOf(BlockA, Gang(7), 0);
            var oursThere = ledger.FearOf(BlockB, Gang(0), 0);

            if (!(oursHere > theirsHere) || theirsHere <= 0f)
                failures.Add("FEAR-001: two houses cannot be feared differently on one block.");
            if (!(oursHere > oursThere) || oursThere <= 0f)
                failures.Add("FEAR-001: the same house is feared identically on every street.");
            if (ledger.FearOf(BlockB, Gang(7), 0) != 0f)
                failures.Add("FEAR-001: a house that did nothing here is feared here.");

            if (Off(ledger.BlockFear(BlockA, 0), oursHere))
                failures.Add("FEAR-001: what the street feels is not the strongest fear on it.");

            var gangs = new List<TerritoryGangValue>();
            ledger.CollectGangs(BlockA, 0, gangs);
            if (gangs.Count != 2 || gangs[0].GangId.Value != 0 || gangs[1].GangId.Value != 7)
                failures.Add("FEAR-001: the published families are wrong or out of order.");
        }

        // ------------------------------------------------------------------- FEAR-002

        /// <summary>
        /// An act is an input, not an authority. One the street cannot pin on a house
        /// frightens nobody in particular - it is never quietly credited to whoever is
        /// nearest - and no act assigns control of anything.
        /// </summary>
        static void AnActNobodyCanPinOnAHouseMakesNobodyFeared(List<string> failures)
        {
            var ledger = new TerritoryFearLedger();
            var unattributed = new TerritoryFearEvent(
                default, BlockA, TerritoryFearCategory.Killing, 1f,
                TerritoryFearVisibility.Public, 0);

            if (unattributed.IsAttributed)
                failures.Add("FEAR-002: an act with no house on it claims one.");

            ledger.Record(unattributed);
            var gangs = new List<TerritoryGangValue>();
            ledger.CollectGangs(BlockA, 0, gangs);
            if (gangs.Count != 0)
                failures.Add("FEAR-002: an unattributed killing made somebody feared.");
            if (ledger.PoliceAttention(BlockA, 0) <= 0f)
                failures.Add("FEAR-002: an unattributed killing did not bring the police.");

            // Category coverage: every category the model names must be worth something
            // definite, or an act would silently do nothing.
            var config = TerritoryFearConfig.Default;
            foreach (var category in TerritoryFearConfig.Categories)
            {
                var impact = config.Of(category);
                if (Math.Abs(impact.Impact) < 0.0001f)
                    failures.Add("FEAR-006: category " + category + " is worth nothing.");
                if (impact.MemoryHalfLifeHours <= 0f)
                    failures.Add("FEAR-006: category " + category + " is remembered for no time.");
            }
        }

        // ------------------------------------------------------------------- FEAR-003

        /// <summary>
        /// Almost every shot in this city is fired in the road, and road space belongs to
        /// no block. An act is still the business of the street it happened in - but a
        /// man merely STANDING in that road holds nothing, and the two questions keep
        /// their separate answers.
        /// </summary>
        static void AnActInTheRoadBelongsToTheStreetBesideIt(List<string> failures)
        {
            var geography = new TerritoryGeography(
                new[] { Definition() },
                new TerritoryGeographySettings(4f, 12f, 24f));

            // The block is x 10..90, z 20..80. This is out in the carriageway beside it.
            var inTheRoad = new TerritoryPoint(100f, 50f);
            if (geography.TryGetBlockAt(inTheRoad, out _))
                failures.Add("FEAR-003: the fixture point is not actually in the road.");
            if (geography.TryResolveStanding(inTheRoad, default, out _))
                failures.Add("FEAR-003: a man standing in the road was handed a block.");

            if (!geography.TryGetBlockNear(inTheRoad, 30f, out var actBlock) ||
                actBlock != BlockA)
                failures.Add("FEAR-003: a shot in the road belongs to no street.");

            // Far enough away and it belongs to nobody after all - the rule has a reach,
            // it is not "nearest block anywhere in the city".
            if (geography.TryGetBlockNear(new TerritoryPoint(400f, 50f), 30f, out _))
                failures.Add("FEAR-003: an act half a district away was pinned on a street.");

            // On the block itself, the two agree.
            if (!geography.TryGetBlockNear(new TerritoryPoint(50f, 50f), 30f, out var onIt) ||
                onIt != BlockA)
                failures.Add("FEAR-003: an act on the block did not land on it.");
        }

        // ------------------------------------------------------------------- FEAR-006

        /// <summary>
        /// A killing on a busy pavement outranks a quiet word in a back room, and every
        /// number that decides it is in the config - no handler holds one of its own.
        /// </summary>
        static void ViolenceIsWeighedByWhatItWasAndWhoSawIt(List<string> failures)
        {
            var loud = new TerritoryFearLedger();
            var quiet = new TerritoryFearLedger();

            loud.Record(Act(0, BlockA, TerritoryFearCategory.Killing,
                visibility: TerritoryFearVisibility.Public, hour: 0));
            quiet.Record(Act(0, BlockA, TerritoryFearCategory.Threat,
                visibility: TerritoryFearVisibility.Hidden, hour: 0));

            if (!(loud.FearOf(BlockA, Gang(0), 0) > quiet.FearOf(BlockA, Gang(0), 0)))
                failures.Add("FEAR-006: a public killing is not worth more than a hidden threat.");

            var seen = new TerritoryFearLedger();
            var hidden = new TerritoryFearLedger();
            seen.Record(Act(0, BlockA, TerritoryFearCategory.Assault,
                visibility: TerritoryFearVisibility.Public, hour: 0));
            hidden.Record(Act(0, BlockA, TerritoryFearCategory.Assault,
                visibility: TerritoryFearVisibility.Hidden, hour: 0));
            if (!(seen.FearOf(BlockA, Gang(0), 0) > hidden.FearOf(BlockA, Gang(0), 0)))
                failures.Add("FEAR-006: who watched made no difference to the same act.");

            // More of the same act is worth more of the same fear.
            var once = new TerritoryFearLedger();
            var again = new TerritoryFearLedger();
            once.Record(Act(0, BlockA, TerritoryFearCategory.Shot, severity: 1f, hour: 0));
            again.Record(Act(0, BlockA, TerritoryFearCategory.Shot, severity: 4f, hour: 0));
            if (!(again.FearOf(BlockA, Gang(0), 0) > once.FearOf(BlockA, Gang(0), 0)))
                failures.Add("FEAR-003: a gunfight is worth no more than a single shot.");

            // ...but not without limit: one long exchange must not pin the street at the
            // cap for weeks, or the loudest possible violence is the only strategy.
            var config = TerritoryFearConfig.Default;
            var massacre = new TerritoryFearLedger(config);
            massacre.Record(Act(0, BlockA, TerritoryFearCategory.Shot, severity: 400f,
                visibility: TerritoryFearVisibility.Public, hour: 0));
            var ceiling = config.Of(TerritoryFearCategory.Shot).Impact * config.MaxSeverity;
            if (Off(massacre.FearOf(BlockA, Gang(0), 0), Math.Min(config.FearCap, ceiling), 0.01f))
                failures.Add("FEAR-006: one long gunfight is worth more than the severity ceiling.");
            if (!(massacre.FearOf(BlockA, Gang(0), 24 * 21) < config.FearCap * 0.5f))
                failures.Add("FEAR-006: a single gunfight still holds the street three weeks on.");

            // Retune the table and the outcome moves with it.
            var tuned = new TerritoryFearLedger(new TerritoryFearConfig(
                new Dictionary<TerritoryFearCategory, TerritoryFearImpact>
                {
                    { TerritoryFearCategory.Threat, new TerritoryFearImpact(60f, 72f, 0f) },
                }));
            tuned.Record(Act(0, BlockA, TerritoryFearCategory.Threat,
                visibility: TerritoryFearVisibility.Public, hour: 0));
            if (Off(tuned.FearOf(BlockA, Gang(0), 0), 60f, 0.01f))
                failures.Add("FEAR-006: the impact table is not what the handlers read.");
        }

        // ------------------------------------------------------------------- FEAR-007

        /// <summary>
        /// An incident at one shop is felt hardest by that shop and, in a smaller way, by
        /// the whole street. A block-wide act is not put through the same reduction twice,
        /// and the next street along hears nothing - Phase 1 has no gossip.
        /// </summary>
        static void AnIncidentAtAPremiseIsFeltByTheWholeBlock(List<string> failures)
        {
            var config = TerritoryFearConfig.Default;
            var ledger = new TerritoryFearLedger(config);

            ledger.Record(Act(0, BlockA, TerritoryFearCategory.Assault, business: Shop, hour: 0));

            var blockFear = ledger.FearOf(BlockA, Gang(0), 0);
            var shopFear = ledger.BusinessFear(BlockA, Shop, Gang(0), 0);
            var barFear = ledger.BusinessFear(BlockA, Bar, Gang(0), 0);

            var full = config.Of(TerritoryFearCategory.Assault).Impact *
                       config.VisibilityWeight(TerritoryFearVisibility.Seen);
            if (Off(blockFear, full * config.PropagationFraction, 0.05f))
                failures.Add("FEAR-007: the street felt the wrong share of an incident at a shop.");
            if (!(shopFear > blockFear))
                failures.Add("FEAR-007: the shop it happened at feels no more than the street.");
            if (Off(barFear, blockFear, 0.05f))
                failures.Add("FEAR-007: a neighbouring premise felt the incident as its own.");
            if (ledger.FearOf(BlockB, Gang(0), 0) != 0f)
                failures.Add("FEAR-007: the incident spread to another block.");

            // A block-wide act lands whole; it is not reduced as if it had a premise.
            var wide = new TerritoryFearLedger(config);
            wide.Record(Act(0, BlockA, TerritoryFearCategory.Assault, hour: 0));
            if (Off(wide.FearOf(BlockA, Gang(0), 0), full, 0.05f))
                failures.Add("FEAR-007: a block-wide act was put through propagation anyway.");
        }

        // ------------------------------------------------------------------- FEAR-008

        /// <summary>
        /// The street remembers. Fear does not depend on anybody still standing there,
        /// and the memory is bounded however long the soak runs.
        /// </summary>
        static void AStreetRemembersAfterTheMenAreGone(List<string> failures)
        {
            var fear = new TerritoryFearLedger();
            var presence = new TerritoryPresenceLedger();

            fear.Record(Act(0, BlockA, TerritoryFearCategory.Killing, hour: 0));
            var remembered = fear.FearOf(BlockA, Gang(0), 0);

            // The men were here, and then they were not. Presence goes to nothing.
            presence.BeginSample();
            presence.Contribute(BlockA, Man(0, 1));
            presence.CommitSample(1f / 60f);
            presence.BeginSample();
            presence.CommitSample(1f / 60f);
            fear.Evaluate(0);

            if (presence.PhysicalOf(BlockA, Gang(0)) != 0f)
                failures.Add("FEAR-009: the men did not actually leave in this fixture.");
            if (Off(fear.FearOf(BlockA, Gang(0), 0), remembered))
                failures.Add("FEAR-009: Presence falling to zero reset the street's memory.");

            // Bounded: a night-long gunfight cannot grow the memory without end.
            var soaked = new TerritoryFearLedger();
            for (var i = 0; i < 500; i++)
                soaked.Record(Act(0, BlockA, TerritoryFearCategory.Shot, hour: i * 0.1));
            var entries = new List<TerritoryFearMemoryEntry>();
            soaked.CollectMemory(BlockA, Gang(0), entries);
            if (entries.Count > TerritoryFearConfig.Default.MemoryEntriesPerGang)
                failures.Add("FEAR-008: what a street remembers grows without bound.");
            if (soaked.FearOf(BlockA, Gang(0), 50) > TerritoryFearConfig.Default.FearCap + 0.01f)
                failures.Add("FEAR-008: fear ran past its cap.");
        }

        // ------------------------------------------------------------------- FEAR-009

        /// <summary>
        /// Memory fades on game hours, and worse acts are remembered longer. A week or two
        /// of quiet barely dents a killing; months erode it.
        /// </summary>
        static void WhatIsRememberedFades(List<string> failures)
        {
            var config = TerritoryFearConfig.Default;
            var ledger = new TerritoryFearLedger(config);
            ledger.Record(Act(0, BlockA, TerritoryFearCategory.Killing, hour: 0));
            var fresh = ledger.FearOf(BlockA, Gang(0), 0);

            if (Off(ledger.FearOf(BlockA, Gang(0), 0), fresh))
                failures.Add("FEAR-009: no game time passed and the memory faded anyway.");

            var half = config.Of(TerritoryFearCategory.Killing).MemoryHalfLifeHours;
            if (Off(ledger.FearOf(BlockA, Gang(0), half), fresh / 2f, 0.05f))
                failures.Add("FEAR-009: one half-life did not halve the memory of a killing.");

            var fortnight = ledger.FearOf(BlockA, Gang(0), 24 * 14);
            if (!(fortnight > fresh * 0.5f))
                failures.Add("FEAR-009: a fortnight of quiet wiped out a killing.");
            if (!(ledger.FearOf(BlockA, Gang(0), 24 * 120) < fresh * 0.05f))
                failures.Add("FEAR-009: months of quiet did not erode a killing.");

            // A threat and a killing do not fade at the same rate.
            var threat = new TerritoryFearLedger(config);
            threat.Record(Act(0, BlockA, TerritoryFearCategory.Threat, hour: 0));
            var threatLeft = threat.FearOf(BlockA, Gang(0), 24 * 14) /
                             Math.Max(0.0001f, threat.FearOf(BlockA, Gang(0), 0));
            var killLeft = fortnight / Math.Max(0.0001f, fresh);
            if (!(killLeft > threatLeft))
                failures.Add("FEAR-008: a threat is remembered as long as a killing.");

            // Spent memory is dropped rather than carried forever.
            var swept = new TerritoryFearLedger(config);
            swept.Record(Act(0, BlockA, TerritoryFearCategory.Threat, hour: 0));
            swept.Evaluate(24 * 365);
            var entries = new List<TerritoryFearMemoryEntry>();
            swept.CollectMemory(BlockA, Gang(0), entries);
            if (entries.Count != 0 || swept.Blocks.Count != 0)
                failures.Add("FEAR-009: a street that has forgotten is still being carried.");
        }

        // ------------------------------------------------------------------- FEAR-010

        /// <summary>
        /// A house that is told no and does nothing about it is worth less on that street.
        /// Answer inside the window and nothing is said; let the window pass and the
        /// street draws its own conclusion - and control is not touched either way.
        /// </summary>
        static void AnUnansweredRefusalCostsTheHouseItsName(List<string> failures)
        {
            var config = TerritoryFearConfig.Default;
            var ignored = new TerritoryFearLedger(config);
            ignored.Record(Act(0, BlockA, TerritoryFearCategory.Assault, hour: 0));
            var standing = ignored.FearOf(BlockA, Gang(0), 0);

            ignored.OpenDefiance(Gang(0), BlockA, Shop, 0);
            var emitted = new List<TerritoryFearEvent>();
            ignored.SweepDefiance(config.DefianceWindowHours / 2f, emitted);
            if (emitted.Count != 0)
                failures.Add("FEAR-010: the street gave up on a refusal before the window closed.");

            ignored.SweepDefiance(config.DefianceWindowHours + 0.1, emitted);
            if (emitted.Count != 1 ||
                emitted[0].Category != TerritoryFearCategory.IgnoredDefiance)
                failures.Add("FEAR-010: an unanswered refusal was never called what it is.");
            if (!(ignored.FearOf(BlockA, Gang(0), config.DefianceWindowHours + 0.1) < standing))
                failures.Add("FEAR-010: letting a shop say no cost the house nothing.");

            var answered = new TerritoryFearLedger(config);
            answered.OpenDefiance(Gang(0), BlockA, Shop, 0);
            if (!answered.AnswerDefiance(Gang(0), Shop))
                failures.Add("FEAR-010: an open refusal could not be answered.");
            var none = new List<TerritoryFearEvent>();
            answered.SweepDefiance(config.DefianceWindowHours * 3f, none);
            if (none.Count != 0)
                failures.Add("FEAR-010: a refusal that was answered was held against the house.");
            if (answered.OpenDefiances.Count != 0)
                failures.Add("FEAR-010: the answered refusal is still open.");
        }

        // ------------------------------------------------------------------- FEAR-013

        /// <summary>
        /// Violence is never free. It buys fear and it buys police attention; while the
        /// law is looking the ground is harder to hold and the next act costs more. The
        /// attention fades, and it can never take a street away from a family entirely.
        /// </summary>
        static void ViolenceBuysPoliceAttention(List<string> failures)
        {
            var config = TerritoryFearConfig.Default;
            var ledger = new TerritoryFearLedger(config);

            if (Off(ledger.PresenceScale(BlockA, 0), 1f))
                failures.Add("FEAR-013: a quiet street already costs something to stand on.");

            ledger.Record(Act(0, BlockA, TerritoryFearCategory.Killing, hour: 0));
            var attention = ledger.PoliceAttention(BlockA, 0);
            if (attention <= 0f)
                failures.Add("FEAR-013: a killing drew no police attention at all.");

            var scale = ledger.PresenceScale(BlockA, 0);
            if (!(scale < 1f))
                failures.Add("FEAR-013: police attention did not make the block harder to hold.");
            if (scale < config.PresenceFloor - 0.0001f)
                failures.Add("FEAR-013: police attention pushed Presence under its floor.");

            // A quiet threat is not a police matter the way a body is.
            var quiet = new TerritoryFearLedger(config);
            quiet.Record(Act(0, BlockA, TerritoryFearCategory.Threat, hour: 0));
            if (!(quiet.PoliceAttention(BlockA, 0) < attention))
                failures.Add("FEAR-013: a whispered threat brought as much law as a killing.");

            // The second killing on a hot street costs more attention than the first.
            var first = ledger.PoliceAttention(BlockA, 0);
            ledger.Record(Act(0, BlockA, TerritoryFearCategory.Killing, hour: 0));
            var second = ledger.PoliceAttention(BlockA, 0) - first;
            if (!(second > 0f))
                failures.Add("FEAR-013: a second killing drew no further attention.");
            if (!(second > attention * (1f + config.PoliceEscalation * 0.5f) * 0.5f))
                failures.Add("FEAR-013: violence does not cost more while the law is looking.");

            // And it fades.
            var later = ledger.PoliceAttention(BlockA, config.PoliceAttentionHalfLifeHours);
            if (!(later < ledger.PoliceAttention(BlockA, 0)))
                failures.Add("FEAR-013: police attention never fades.");
            if (!(ledger.PresenceScale(BlockA, 24 * 30) > scale))
                failures.Add("FEAR-013: a street stays hot forever.");
        }

        // ------------------------------------------------------------------- FEAR-011

        /// <summary>
        /// The player reads a word and a tone, never the number, and the words are the
        /// ones a street would use.
        /// </summary>
        static void ThePlayerReadsWordsAndATone(List<string> failures)
        {
            var scale = TerritoryPresentationProfile.Default.Fear;
            if (scale.Describe(null) != scale.UnknownLabel ||
                scale.Describe(0f) != "Calm" ||
                scale.Describe(scale.WeakAt) != "Uneasy" ||
                scale.Describe(scale.ModerateAt) != "Afraid" ||
                scale.Describe(scale.StrongAt) != "Terrified")
                failures.Add("FEAR-011: the fear bands do not read in the street's words.");

            var state = new TerritorySimulationState(new[] { Definition() });
            var truth = new TerritoryTruthQuery(state);
            state.SetSignals(BlockA, new TerritoryBlockSignals(
                localFear: 80f,
                control: TerritoryControlState.Contested,
                gangs: new[]
                {
                    new TerritoryGangSignals(Gang(0), 10f, 0f, 80f),
                    new TerritoryGangSignals(Gang(7), 10f, 0f, 12f),
                }));

            var player = new TerritoryPlayerQuery(
                truth, Gang(0), TerritoryPresentationProfile.Default);
            if (!player.TryGetBlock(BlockA, out var view))
                failures.Add("FEAR-011: the player cannot read the block at all.");
            else
            {
                if (view.FearOfUs != "Terrified")
                    failures.Add("FEAR-011: the street's fear of us is not described.");
                if (view.OwnerTone != TerritoryOwnerTone.Cowed)
                    failures.Add("FEAR-011: the owner tone hint does not follow the fear.");
                if (view.LocalFear != "Terrified")
                    failures.Add("FEAR-011: what the street feels at all is not described.");
                foreach (var text in new[] { view.FearOfUs, view.LocalFear })
                    if (text.Contains("80"))
                        failures.Add("FEAR-011: the player page printed the exact fear value.");
            }

            var calm = new TerritoryPresentationProjector(TerritoryPresentationProfile.Default);
            var quiet = calm.Project(
                new TerritoryBlockTruth(Definition(), default, null,
                    new TerritoryBlockSignals(gangs: new[]
                    {
                        new TerritoryGangSignals(Gang(0), 0f, 0f, 3f),
                    })),
                new TerritoryBlockSignals(gangs: new[]
                {
                    new TerritoryGangSignals(Gang(0), 0f, 0f, 3f),
                }),
                Gang(0));
            if (quiet.OwnerTone != TerritoryOwnerTone.Wary)
                failures.Add("FEAR-011: a barely-frightened street reads as something else.");
        }

        // ------------------------------------------------------------ the shared store

        /// <summary>
        /// Fear, Presence and the deeds are three numbers with three owners in one record.
        /// Writing any one of them must hand the other two back exactly as they were.
        /// </summary>
        static void FearRidesTheStoreWithoutWipingIt(List<string> failures)
        {
            var scratch = new List<TerritoryGangSignals>();
            var presence = new List<TerritoryGangPresence>
            {
                new TerritoryGangPresence(Gang(0), 40f, 0f, 40f),
            };
            var withPresence = TerritoryPresenceSignals.Merge(
                TerritoryBlockSignals.Empty, presence, scratch);

            var deeds = new TerritoryControlDerivation.Tally();
            deeds.Add(0);
            var withDeeds = TerritoryControlDerivation.Signals(deeds, withPresence, scratch);

            var fear = new List<TerritoryGangValue> { new TerritoryGangValue(Gang(0), 55f) };
            var withFear = TerritoryPresenceSignals.Merge(
                withDeeds, fear, TerritorySignalChannel.Fear, scratch);

            if (!withFear.TryGetGang(Gang(0), out var all))
                failures.Add("Store: the family fell out of the block on the way through.");
            else if (Off(all.Presence, 40f) || Off(all.Influence, 100f) || Off(all.Fear, 55f))
                failures.Add("Store: writing Fear wiped Presence or the deeds.");
            if (withFear.Control != TerritoryControlState.Controlled)
                failures.Add("Store: writing Fear changed who holds the block.");

            // And the control pass, running next, hands Fear straight back.
            var again = TerritoryControlDerivation.Signals(deeds, withFear, scratch);
            if (!again.TryGetGang(Gang(0), out var after) || Off(after.Fear, 55f) ||
                Off(after.Presence, 40f))
                failures.Add("Store: the control pass wiped what the street is afraid of.");
            if (!TerritoryControlDerivation.Same(withFear, again))
                failures.Add("Store: an unchanged block would be rewritten every tick.");
        }

        // ------------------------------------------------------------------- fixtures

        static TerritoryGangId Gang(int id) => new TerritoryGangId(id);

        static TerritoryFearEvent Act(
            int gang,
            TerritoryBlockId blockId,
            TerritoryFearCategory category,
            float severity = 1f,
            TerritoryFearVisibility visibility = TerritoryFearVisibility.Seen,
            double hour = 0,
            TerritoryBusinessId business = default) =>
            new TerritoryFearEvent(
                Gang(gang), blockId, category, severity, visibility, hour, business);

        static TerritoryActorObservation Man(int gang, int character) =>
            new TerritoryActorObservation(
                new TerritoryCharacterId(character),
                new TerritoryGangId(gang),
                TerritoryCommandNodeId.Crew(1),
                "man " + character,
                "gang " + gang,
                false,
                TerritoryRank.Hood,
                TerritoryActorActivity.Stationed);

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
    }
}
