using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Headless contracts for GAN-46 / SIM-001 through SIM-008.</summary>
    public static class TerritoryFoundationTests
    {
        static readonly TerritoryBlockId BlockId =
            new TerritoryBlockId("core:1987:1:2:3:4:5:res");

        public static List<string> Run()
        {
            var failures = new List<string>();

            StableIdsAdaptExistingIdentity(failures);
            CommandsRejectAtomicallyAndPhysicalAcceptanceStaysPending(failures);
            PresentationProfilesCannotChangeTruth(failures);
            PlayerKnowledgeIsSeparateFromDebugTruth(failures);
            EventsAnnounceButDoNotOwnState(failures);
            SchedulerIsFrameIndependentAndPauseSafe(failures);
            ControlIsReadOffTheDeedsOnTheBlock(failures);

            return failures;
        }

        /// <summary>
        /// Ground is taken premise by premise, so control is a reading of the deeds on
        /// the block and nothing else. One family on it holds it, two make it contested,
        /// no deeds make it nobody's - and a control pass must carry forward the signals
        /// it does not own.
        /// </summary>
        static void ControlIsReadOffTheDeedsOnTheBlock(List<string> failures)
        {
            var scratch = new List<TerritoryGangSignals>();
            var empty = new TerritoryControlDerivation.Tally();
            if (TerritoryControlDerivation.Read(empty, out var nobody) !=
                    TerritoryControlState.Uncontrolled || nobody != -1)
                failures.Add("Control: a block with no deeds on it is not uncontrolled.");

            var ours = new TerritoryControlDerivation.Tally();
            ours.Add(0);
            ours.Add(0);
            if (TerritoryControlDerivation.Read(ours, out var holder) !=
                    TerritoryControlState.Controlled || holder != 0)
                failures.Add("Control: one family holding every premise does not hold the block.");
            if (System.Math.Abs(TerritoryControlDerivation.ShareOf(ours, 0) - 100f) > 0.001f ||
                TerritoryControlDerivation.ShareOf(ours, 3) != 0f)
                failures.Add("Control: a family's share of the block is wrong.");

            var pushed = new TerritoryControlDerivation.Tally();
            pushed.Add(0);
            pushed.Add(0);
            pushed.Add(4);
            if (TerritoryControlDerivation.Read(pushed, out var leading) !=
                    TerritoryControlState.Contested || leading != 0)
                failures.Add("Control: a second house on the block did not make it contested.");

            var tied = new TerritoryControlDerivation.Tally();
            tied.Add(1);
            tied.Add(4);
            if (TerritoryControlDerivation.Read(tied, out var neither) !=
                    TerritoryControlState.Contested || neither != -1)
                failures.Add("Control: an even split named a leader anyway.");

            // Fear and business compliance belong to other tickets: a control pass must
            // hand them back untouched.
            var previous = new TerritoryBlockSignals(
                localFear: 40f, businessCompliance: 30f,
                compliantBusinesses: 2, totalBusinesses: 5,
                control: TerritoryControlState.Unknown);
            var next = TerritoryControlDerivation.Signals(pushed, previous, scratch);
            if (next.LocalFear != previous.LocalFear ||
                next.BusinessCompliance != previous.BusinessCompliance ||
                next.CompliantBusinesses != 2 || next.TotalBusinesses != 5)
                failures.Add("Control: a control pass wiped signals it does not own.");
            if (next.Control != TerritoryControlState.Contested ||
                next.LeadingGangId.Value != 0 || next.Gangs.Count != 2)
                failures.Add("Control: the published signals do not match the deeds.");

            var repeat = TerritoryControlDerivation.Signals(pushed, next, scratch);
            if (!TerritoryControlDerivation.Same(next, repeat))
                failures.Add("Control: an unchanged block would be rewritten every tick.");
            if (TerritoryControlDerivation.Same(next,
                    TerritoryControlDerivation.Signals(ours, next, scratch)))
                failures.Add("Control: a changed block would not be rewritten.");
        }

        static TerritoryBlockDefinition Definition() =>
            new TerritoryBlockDefinition(
                BlockId,
                12,
                TerritoryIdentity.CoreNeighborhood(1987, 1),
                "Downtown",
                "Downtown Block 01",
                new TerritoryBounds(10f, 20f, 80f, 60f),
                "CoreTerritoryPlan.StableId");

        static void StableIdsAdaptExistingIdentity(List<string> failures)
        {
            const string existing = "core:1987:1:2:3:4:5:res";
            if (TerritoryIdentity.ExistingBlock(existing).Value != existing)
                failures.Add("Identity: Core StableId was replaced instead of adapted.");

            var a = TerritoryIdentity.GeneratedBusiness(7, 3, 120, -40, 1);
            var b = TerritoryIdentity.GeneratedBusiness(7, 3, 120, -40, 1);
            var collision = TerritoryIdentity.GeneratedBusiness(7, 3, 120, -40, 1, 1);
            if (a != b || a == collision)
                failures.Add("Identity: generated business IDs are not stable/distinct.");
            if (default(TerritoryBlockId).IsValid || default(TerritoryGangId).IsValid)
                failures.Add("Identity: default IDs are being treated as real entities.");
        }

        static void CommandsRejectAtomicallyAndPhysicalAcceptanceStaysPending(
            List<string> failures)
        {
            var state = new TerritorySimulationState(new[] { Definition() });
            var executor = new TestExecutor(state);
            var commands = new TerritoryCommandGateway(executor);

            var before = state.Version;
            var invalid = commands.Submit(new AssignBlockResponsibilityCommand(
                new TerritoryBlockId("missing"),
                new TerritoryGangId(0),
                TerritoryCommandNodeId.Crew(4)));
            if (invalid.Status != TerritoryCommandStatus.Rejected || state.Version != before)
                failures.Add("Commands: an invalid assignment partially changed state.");

            var move = commands.Submit(new MoveTacticalGroupCommand(
                TerritoryCommandNodeId.Crew(4), new TerritoryPoint(100f, 200f)));
            if (move.Status != TerritoryCommandStatus.Pending || !move.WasAccepted)
                failures.Add("Commands: an accepted physical order did not remain pending.");
            if (state.Version != before)
                failures.Add("Commands: move acceptance fabricated a territory state change.");

            var assigned = commands.Submit(new AssignBlockResponsibilityCommand(
                BlockId,
                new TerritoryGangId(0),
                TerritoryCommandNodeId.Crew(4),
                lieutenantId: new TerritoryCharacterId(9)));
            if (assigned.Status != TerritoryCommandStatus.Succeeded)
                failures.Add("Commands: valid block responsibility was not applied.");

            var truth = new TerritoryTruthQuery(state);
            if (!truth.TryGetBlock(BlockId, out var block) ||
                block.Responsibility.Responsibility.CommandNodeId !=
                    TerritoryCommandNodeId.Crew(4))
                failures.Add("Commands: responsibility did not use the canonical block ID.");
        }

        static void PresentationProfilesCannotChangeTruth(List<string> failures)
        {
            var state = StateWithSignals();
            var truth = new TerritoryTruthQuery(state);
            var viewer = new TerritoryGangId(0);

            var strongAtSixty = TerritoryPresentationProfile.Default;
            var strongAtEighty = new TerritoryPresentationProfile(
                new TerritoryQualitativeScale(0.01f, 25f, 80f),
                new TerritoryQualitativeScale(0.01f, 25f, 80f),
                new TerritoryQualitativeScale(0.01f, 35f, 80f),
                new TerritoryQualitativeScale(0.01f, 25f, 80f));

            var first = new TerritoryPlayerQuery(truth, viewer, strongAtSixty);
            var second = new TerritoryPlayerQuery(truth, viewer, strongAtEighty);
            first.TryGetBlock(BlockId, out var a);
            second.TryGetBlock(BlockId, out var b);

            if (a == null || b == null || a.Presence != "Strong" || b.Presence != "Moderate")
                failures.Add("Presentation: configurable thresholds did not change only the label.");
            if (a?.Businesses != "7/9 compliant")
                failures.Add("Presentation: business compliance count was not projected.");

            truth.TryGetBlock(BlockId, out var exact);
            if (exact == null || !exact.Signals.TryGetGang(viewer, out var gang) ||
                gang.Presence != 67.4f)
                failures.Add("Presentation: rebuilding with a second profile changed exact truth.");
        }

        static void PlayerKnowledgeIsSeparateFromDebugTruth(List<string> failures)
        {
            var state = StateWithSignals();
            var truth = new TerritoryTruthQuery(state);
            var viewer = new TerritoryGangId(0);
            var player = new TerritoryPlayerQuery(
                truth,
                viewer,
                TerritoryPresentationProfile.Default,
                new HideRivalsKnowledgeFilter());

            player.TryGetBlock(BlockId, out var visible);
            truth.TryGetBlock(BlockId, out var exact);

            if (visible == null || visible.RivalActivity != "Unknown")
                failures.Add("Knowledge: normal player query bypassed the rival filter.");
            if (exact == null || exact.Signals.Gangs.Count != 2 ||
                exact.Signals.Gangs[1].Influence != 47.2f)
                failures.Add("Knowledge: filtering duplicated or changed debug truth.");
        }

        static void EventsAnnounceButDoNotOwnState(List<string> failures)
        {
            var state = new TerritorySimulationState(new[] { Definition() });
            state.AssignResponsibility(BlockId, new TerritoryResponsibility(
                new TerritoryGangId(0),
                default,
                new TerritoryCharacterId(9),
                TerritoryCommandNodeId.Crew(4)));

            var events = new TerritoryEventStream();
            var calls = 0;
            System.Action<BlockControlChanged> subscriber = _ => calls++;
            events.BlockControl += subscriber;
            events.Publish(new BlockControlChanged(
                BlockId, default, new TerritoryGangId(0),
                TerritoryControlState.Influenced, 12.0));
            events.BlockControl -= subscriber;

            // Recreate the consumer after the event. It must recover current state by query.
            var recreated = new TerritoryTruthQuery(state);
            recreated.TryGetBlock(BlockId, out var block);
            if (calls != 1 || events.Recent.Count != 1)
                failures.Add("Events: typed publication/history did not receive the change.");
            if (block == null ||
                block.Responsibility.Responsibility.LieutenantId !=
                    new TerritoryCharacterId(9))
                failures.Add("Events: recreating a subscriber lost authoritative state.");
        }

        static void SchedulerIsFrameIndependentAndPauseSafe(List<string> failures)
        {
            var oneFrame = Scheduler(out var oneCounts);
            var manyFrames = Scheduler(out var manyCounts);

            oneFrame.AdvanceTo(0.0);
            oneFrame.AdvanceTo(2.0);

            manyFrames.AdvanceTo(0.0);
            for (var i = 1; i <= 40; i++)
                manyFrames.AdvanceTo(i * 0.05);

            for (var i = 0; i < oneCounts.Length; i++)
                if (oneCounts[i] != manyCounts[i])
                    failures.Add($"Scheduler: channel {i} depends on frame count " +
                                 $"({oneCounts[i]} vs {manyCounts[i]}).");

            var beforePause = oneCounts[(int)TerritoryTickChannel.PhysicalPresence];
            if (oneFrame.AdvanceTo(2.0) != 0 ||
                oneCounts[(int)TerritoryTickChannel.PhysicalPresence] != beforePause)
                failures.Add("Scheduler: unchanged/paused game time emitted ticks.");

            if (oneCounts[(int)TerritoryTickChannel.PhysicalPresence] != 20 ||
                oneCounts[(int)TerritoryTickChannel.Fear] != 2)
                failures.Add("Scheduler: independent configured cadences were not respected.");

            var forcedBefore = oneCounts[(int)TerritoryTickChannel.Business];
            oneFrame.Force(TerritoryTickChannel.Business);
            if (oneCounts[(int)TerritoryTickChannel.Business] != forcedBefore + 1)
                failures.Add("Scheduler: developer force evaluation did not fire.");
        }

        static TerritorySimulationState StateWithSignals()
        {
            var state = new TerritorySimulationState(new[] { Definition() });
            state.SetSignals(BlockId, new TerritoryBlockSignals(
                localFear: 52.1f,
                businessCompliance: 73f,
                compliantBusinesses: 7,
                totalBusinesses: 9,
                control: TerritoryControlState.Controlled,
                leadingGangId: new TerritoryGangId(0),
                gangs: new[]
                {
                    new TerritoryGangSignals(new TerritoryGangId(0), 67.4f, 67.4f),
                    new TerritoryGangSignals(new TerritoryGangId(1), 21.5f, 47.2f),
                }));
            return state;
        }

        static TerritorySimulationScheduler Scheduler(out int[] counts)
        {
            counts = new int[5];
            var captured = counts;
            var scheduler = new TerritorySimulationScheduler();
            scheduler.SetCadence(TerritoryTickChannel.PhysicalPresence, 0.1);
            scheduler.SetCadence(TerritoryTickChannel.ResidualPresence, 0.25);
            scheduler.SetCadence(TerritoryTickChannel.Fear, 1.0);
            scheduler.SetCadence(TerritoryTickChannel.Business, 4.0);
            scheduler.SetCadence(TerritoryTickChannel.DerivedControl, 0.5);
            scheduler.Ticked += tick => captured[(int)tick.Channel]++;
            return scheduler;
        }

        sealed class HideRivalsKnowledgeFilter : ITerritoryKnowledgeFilter
        {
            public TerritoryBlockSignals Observe(
                TerritoryBlockTruth truth, TerritoryGangId viewingGangId)
            {
                var own = new List<TerritoryGangSignals>();
                if (truth.Signals.TryGetGang(viewingGangId, out var signals))
                    own.Add(signals);
                return new TerritoryBlockSignals(
                    truth.Signals.LocalFear,
                    truth.Signals.BusinessCompliance,
                    truth.Signals.CompliantBusinesses,
                    truth.Signals.TotalBusinesses,
                    truth.Signals.Control,
                    truth.Signals.LeadingGangId,
                    own);
            }
        }

        sealed class TestExecutor : ITerritoryCommandExecutor
        {
            readonly TerritorySimulationState state;

            public TestExecutor(TerritorySimulationState state) => this.state = state;

            public TerritoryCommandExecution Execute(AssignBlockResponsibilityCommand command)
            {
                if (!command.BlockId.IsValid ||
                    !state.TryGetDefinition(command.BlockId, out _))
                    return TerritoryCommandExecution.Reject("Unknown block.");
                var value = new TerritoryResponsibility(
                    command.GangId, command.BossId, command.LieutenantId,
                    command.CommandNodeId);
                return state.AssignResponsibility(command.BlockId, value)
                    ? TerritoryCommandExecution.Succeed()
                    : TerritoryCommandExecution.Reject("Refused.");
            }

            public TerritoryCommandExecution Execute(MoveTacticalGroupCommand command) =>
                command.GroupId.IsValid && command.Destination.IsFinite
                    ? TerritoryCommandExecution.Pending()
                    : TerritoryCommandExecution.Reject("Invalid movement.");

            public TerritoryCommandExecution Execute(AssignHoodToBossCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(AssignHoodToLieutenantCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(OperateInBlockCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(ApproachBusinessCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(DemandProtectionCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(ThreatenBusinessOwnerCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
            public TerritoryCommandExecution Execute(CollectDuesCommand command) =>
                TerritoryCommandExecution.Reject("Not in fixture.");
        }
    }
}
