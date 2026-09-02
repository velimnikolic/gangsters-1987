using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    public readonly struct AssignHoodToBossCommand
    {
        public AssignHoodToBossCommand(
            TerritoryCharacterId hoodId, TerritoryCharacterId bossId)
        {
            HoodId = hoodId;
            BossId = bossId;
        }

        public TerritoryCharacterId HoodId { get; }
        public TerritoryCharacterId BossId { get; }
    }

    public readonly struct AssignHoodToLieutenantCommand
    {
        public AssignHoodToLieutenantCommand(
            TerritoryCharacterId hoodId, TerritoryCharacterId lieutenantId)
        {
            HoodId = hoodId;
            LieutenantId = lieutenantId;
        }

        public TerritoryCharacterId HoodId { get; }
        public TerritoryCharacterId LieutenantId { get; }
    }

    public readonly struct AssignBlockResponsibilityCommand
    {
        public AssignBlockResponsibilityCommand(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId commandNodeId,
            TerritoryCharacterId bossId = default,
            TerritoryCharacterId lieutenantId = default)
        {
            BlockId = blockId;
            GangId = gangId;
            CommandNodeId = commandNodeId;
            BossId = bossId;
            LieutenantId = lieutenantId;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCommandNodeId CommandNodeId { get; }
        public TerritoryCharacterId BossId { get; }
        public TerritoryCharacterId LieutenantId { get; }
    }

    public enum TacticalMovementMode
    {
        StreetOrder,
        DirectMarch,
    }

    public readonly struct MoveTacticalGroupCommand
    {
        public MoveTacticalGroupCommand(
            TerritoryCommandNodeId groupId,
            TerritoryPoint destination,
            TerritoryBlockId destinationBlockId = default,
            bool run = false,
            TacticalMovementMode mode = TacticalMovementMode.StreetOrder)
        {
            GroupId = groupId;
            Destination = destination;
            DestinationBlockId = destinationBlockId;
            Run = run;
            Mode = mode;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryPoint Destination { get; }
        public TerritoryBlockId DestinationBlockId { get; }
        public bool Run { get; }
        public TacticalMovementMode Mode { get; }
    }

    public readonly struct OperateInBlockCommand
    {
        public OperateInBlockCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    public readonly struct ApproachBusinessCommand
    {
        public ApproachBusinessCommand(
            TerritoryCommandNodeId groupId, TerritoryBusinessId businessId)
            : this(groupId, businessId, TerritoryRacketIntent.Approach)
        {
        }

        public ApproachBusinessCommand(
            TerritoryCommandNodeId groupId, TerritoryBusinessId businessId,
            TerritoryRacketIntent followUp)
        {
            GroupId = groupId;
            BusinessId = businessId;
            FollowUp = followUp;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBusinessId BusinessId { get; }

        /// <summary>What the walk is FOR. Approach is the walk alone; Demand and
        /// Threaten mean the men put it to the owner the moment they arrive, so an order
        /// given from across the city is one order, not a walk and a second click.</summary>
        public TerritoryRacketIntent FollowUp { get; }
    }

    /// <summary>Send a crew on a collection round (ECON-004): every shop on the block
    /// that pays this family, door to door, and the take carried home to the front.</summary>
    public readonly struct CollectDuesCommand
    {
        public CollectDuesCommand(TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    /// <summary>
    /// SHAKE DOWN A WHOLE BLOCK: the crew walks every shop on it that does not pay us
    /// yet and puts it to each owner in turn. What happens on a no is the crew's policy,
    /// not this command's - the order is the same order whoever carries it.
    /// </summary>
    public readonly struct ShakeDownBlockCommand
    {
        public ShakeDownBlockCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    /// <summary>
    /// LEAN ON THE HOLDOUTS: the same walk against the doors that refused us or are
    /// wavering, and a threat at each rather than a question. Fear up, heat up.
    /// </summary>
    public readonly struct LeanOnHoldoutsCommand
    {
        public LeanOnHoldoutsCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    public readonly struct DemandProtectionCommand
    {
        public DemandProtectionCommand(
            TerritoryCharacterId actorId, TerritoryBusinessId businessId)
        {
            ActorId = actorId;
            BusinessId = businessId;
        }

        public TerritoryCharacterId ActorId { get; }
        public TerritoryBusinessId BusinessId { get; }
    }

    public readonly struct ThreatenBusinessOwnerCommand
    {
        public ThreatenBusinessOwnerCommand(
            TerritoryCharacterId actorId, TerritoryBusinessId businessId)
        {
            ActorId = actorId;
            BusinessId = businessId;
        }

        public TerritoryCharacterId ActorId { get; }
        public TerritoryBusinessId BusinessId { get; }
    }

    public enum TerritoryCommandStatus
    {
        Rejected,
        Accepted,
        Pending,
        Succeeded,
        Failed,
    }

    /// <summary>Executor response before the gateway assigns a stable command sequence.</summary>
    public readonly struct TerritoryCommandExecution
    {
        TerritoryCommandExecution(TerritoryCommandStatus status, string reason)
        {
            Status = status;
            Reason = reason ?? "";
        }

        public TerritoryCommandStatus Status { get; }
        public string Reason { get; }

        public static TerritoryCommandExecution Reject(string reason) =>
            new TerritoryCommandExecution(TerritoryCommandStatus.Rejected, reason);

        public static TerritoryCommandExecution Accept(string note = "") =>
            new TerritoryCommandExecution(TerritoryCommandStatus.Accepted, note);

        public static TerritoryCommandExecution Pending(string note = "") =>
            new TerritoryCommandExecution(TerritoryCommandStatus.Pending, note);

        public static TerritoryCommandExecution Succeed(string note = "") =>
            new TerritoryCommandExecution(TerritoryCommandStatus.Succeeded, note);

        public static TerritoryCommandExecution Fail(string reason) =>
            new TerritoryCommandExecution(TerritoryCommandStatus.Failed, reason);
    }

    public readonly struct TerritoryCommandResult
    {
        public TerritoryCommandResult(
            long commandId, TerritoryCommandStatus status, string reason)
        {
            CommandId = commandId;
            Status = status;
            Reason = reason ?? "";
        }

        public long CommandId { get; }
        public TerritoryCommandStatus Status { get; }
        public string Reason { get; }
        public bool WasAccepted => Status != TerritoryCommandStatus.Rejected;
        public bool IsTerminal => Status == TerritoryCommandStatus.Rejected ||
                                  Status == TerritoryCommandStatus.Succeeded ||
                                  Status == TerritoryCommandStatus.Failed;
    }

    /// <summary>
    /// Focused Phase-1 command handler contract. Explicit overloads keep command types
    /// visible and avoid introducing a project-wide generic message bus.
    /// </summary>
    public interface ITerritoryCommandExecutor
    {
        TerritoryCommandExecution Execute(AssignHoodToBossCommand command);
        TerritoryCommandExecution Execute(AssignHoodToLieutenantCommand command);
        TerritoryCommandExecution Execute(AssignBlockResponsibilityCommand command);
        TerritoryCommandExecution Execute(MoveTacticalGroupCommand command);
        TerritoryCommandExecution Execute(OperateInBlockCommand command);
        TerritoryCommandExecution Execute(ApproachBusinessCommand command);
        TerritoryCommandExecution Execute(DemandProtectionCommand command);
        TerritoryCommandExecution Execute(ThreatenBusinessOwnerCommand command);
        TerritoryCommandExecution Execute(CollectDuesCommand command);
        TerritoryCommandExecution Execute(ShakeDownBlockCommand command);
        TerritoryCommandExecution Execute(LeanOnHoldoutsCommand command);
    }

    /// <summary>
    /// The player/UI mutation boundary. It validates through the authoritative executor,
    /// records rejection/pending/success, and never turns command acceptance into a
    /// fabricated territory result.
    /// </summary>
    public sealed class TerritoryCommandGateway
    {
        const int HistoryLimit = 512;

        readonly ITerritoryCommandExecutor executor;
        readonly Dictionary<long, TerritoryCommandResult> results =
            new Dictionary<long, TerritoryCommandResult>();
        readonly Queue<long> resultOrder = new Queue<long>();
        long nextCommandId = 1;

        public TerritoryCommandGateway(ITerritoryCommandExecutor executor) =>
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));

        public event Action<TerritoryCommandResult> StatusChanged;

        public TerritoryCommandResult Submit(AssignHoodToBossCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(AssignHoodToLieutenantCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(AssignBlockResponsibilityCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(MoveTacticalGroupCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(OperateInBlockCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(ApproachBusinessCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(DemandProtectionCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(ThreatenBusinessOwnerCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(CollectDuesCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(ShakeDownBlockCommand command) =>
            Record(executor.Execute(command));

        public TerritoryCommandResult Submit(LeanOnHoldoutsCommand command) =>
            Record(executor.Execute(command));

        public bool TryGet(long commandId, out TerritoryCommandResult result) =>
            results.TryGetValue(commandId, out result);

        internal bool Resolve(long commandId, TerritoryCommandStatus status, string reason = "")
        {
            if (!results.TryGetValue(commandId, out var current) || current.IsTerminal ||
                status == TerritoryCommandStatus.Rejected)
                return false;

            var resolved = new TerritoryCommandResult(commandId, status, reason);
            results[commandId] = resolved;
            StatusChanged?.Invoke(resolved);
            return true;
        }

        TerritoryCommandResult Record(TerritoryCommandExecution execution)
        {
            var result = new TerritoryCommandResult(
                nextCommandId++, execution.Status, execution.Reason);
            results.Add(result.CommandId, result);
            resultOrder.Enqueue(result.CommandId);
            if (resultOrder.Count > HistoryLimit)
                results.Remove(resultOrder.Dequeue());
            StatusChanged?.Invoke(result);
            return result;
        }
    }
}
