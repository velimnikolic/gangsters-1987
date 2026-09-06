using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// WHOSE ORDER THIS IS. Every command carries the house that filed it, because
    /// twenty-one families run on this one gateway and "is this the player?" is not a
    /// question any rule below it is allowed to ask.
    ///
    /// It is set in exactly one place for the player - PlayerCommands.Stamp, in the
    /// Gameplay layer - and by a mind for everybody else. The gateway refuses an order
    /// with no house named on it.
    /// </summary>
    public interface ITerritoryHouseCommand
    {
        TerritoryGangId House { get; set; }
    }

    public struct AssignHoodToBossCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public AssignHoodToBossCommand(
            TerritoryCharacterId hoodId, TerritoryCharacterId bossId)
        {
            House = default;
            HoodId = hoodId;
            BossId = bossId;
        }

        public TerritoryCharacterId HoodId { get; }
        public TerritoryCharacterId BossId { get; }
    }

    public struct AssignHoodToLieutenantCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public AssignHoodToLieutenantCommand(
            TerritoryCharacterId hoodId, TerritoryCharacterId lieutenantId)
        {
            House = default;
            HoodId = hoodId;
            LieutenantId = lieutenantId;
        }

        public TerritoryCharacterId HoodId { get; }
        public TerritoryCharacterId LieutenantId { get; }
    }

    public struct AssignBlockResponsibilityCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public AssignBlockResponsibilityCommand(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId commandNodeId,
            TerritoryCharacterId bossId = default,
            TerritoryCharacterId lieutenantId = default)
        {
            House = default;
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

    public struct MoveTacticalGroupCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public MoveTacticalGroupCommand(
            TerritoryCommandNodeId groupId,
            TerritoryPoint destination,
            TerritoryBlockId destinationBlockId = default,
            bool run = false,
            TacticalMovementMode mode = TacticalMovementMode.StreetOrder)
        {
            House = default;
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

    public struct OperateInBlockCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public OperateInBlockCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            House = default;
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    public struct ApproachBusinessCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public ApproachBusinessCommand(
            TerritoryCommandNodeId groupId, TerritoryBusinessId businessId)
            : this(groupId, businessId, TerritoryRacketIntent.Approach)
        {
        }

        public ApproachBusinessCommand(
            TerritoryCommandNodeId groupId, TerritoryBusinessId businessId,
            TerritoryRacketIntent followUp, TerritoryBlockId blockScope = default,
            bool restrictToResponsible = false)
        {
            House = default;
            GroupId = groupId;
            BusinessId = businessId;
            FollowUp = followUp;
            BlockScope = blockScope;
            RestrictToResponsible = restrictToResponsible;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBusinessId BusinessId { get; }

        /// <summary>What the walk is FOR. Approach is the walk alone; Demand and
        /// Threaten mean the men put it to the owner the moment they arrive, so an order
        /// given from across the city is one order, not a walk and a second click.</summary>
        public TerritoryRacketIntent FollowUp { get; }
        public TerritoryBlockId BlockScope { get; }
        public bool RestrictToResponsible { get; }
    }

    /// <summary>Send a crew on a collection round (ECON-004): every shop on the block
    /// that pays this family, door to door, and the take carried home to the front.</summary>
    public struct CollectDuesCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        /// <summary>The gateway's receipt, stamped before the street is asked to carry
        /// the order. Most commands finish inside Submit; a bag detail can first have
        /// to cross the headquarters door, so the runtime needs the original receipt
        /// to close rather than inventing a second command when it finally leaves.</summary>
        public long CommandId { get; internal set; }

        public CollectDuesCommand(TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            House = default;
            CommandId = 0;
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
    public struct ShakeDownBlockCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public ShakeDownBlockCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            House = default;
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
    public struct LeanOnHoldoutsCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public LeanOnHoldoutsCommand(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId)
        {
            House = default;
            GroupId = groupId;
            BlockId = blockId;
        }

        public TerritoryCommandNodeId GroupId { get; }
        public TerritoryBlockId BlockId { get; }
    }

    public struct DemandProtectionCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public DemandProtectionCommand(
            TerritoryCharacterId actorId, TerritoryBusinessId businessId)
        {
            House = default;
            ActorId = actorId;
            BusinessId = businessId;
        }

        public TerritoryCharacterId ActorId { get; }
        public TerritoryBusinessId BusinessId { get; }
    }

    public struct ThreatenBusinessOwnerCommand : ITerritoryHouseCommand
    {
        /// <summary>The family that filed it (ITerritoryHouseCommand).</summary>
        public TerritoryGangId House { get; set; }

        public ThreatenBusinessOwnerCommand(
            TerritoryCharacterId actorId, TerritoryBusinessId businessId)
        {
            House = default;
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

        /// <summary>An order with nobody's name on it. The gateway is the one wall
        /// every house's orders come through, so it is the one place this is
        /// caught.</summary>
        public const string NoHouse = "no house named on the order";

        readonly ITerritoryCommandExecutor executor;
        readonly Dictionary<long, TerritoryCommandResult> results =
            new Dictionary<long, TerritoryCommandResult>();
        readonly Queue<long> resultOrder = new Queue<long>();
        long nextCommandId = 1;

        public TerritoryCommandGateway(ITerritoryCommandExecutor executor) =>
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));

        public event Action<TerritoryCommandResult> StatusChanged;

        public TerritoryCommandResult Submit(AssignHoodToBossCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(AssignHoodToLieutenantCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(AssignBlockResponsibilityCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(MoveTacticalGroupCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(OperateInBlockCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(ApproachBusinessCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(DemandProtectionCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(ThreatenBusinessOwnerCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(CollectDuesCommand command)
        {
            if (!Named(command))
                return Unnamed();

            // A collection may wait for the bag detail to come through a door. Give
            // the executor the receipt up front so that deferred attempt can resolve
            // THIS history row when it starts or fails.
            var commandId = nextCommandId++;
            command.CommandId = commandId;
            return Record(commandId, executor.Execute(command));
        }

        public TerritoryCommandResult Submit(ShakeDownBlockCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

        public TerritoryCommandResult Submit(LeanOnHoldoutsCommand command) =>
            Named(command) ? Record(executor.Execute(command)) : Unnamed();

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

        /// <summary>Whether an order says whose it is. A struct constraint, so the
        /// check costs no boxing on a path every order of every house goes through.
        /// </summary>
        static bool Named<T>(T command) where T : struct, ITerritoryHouseCommand =>
            command.House.IsValid;

        TerritoryCommandResult Unnamed() =>
            Record(TerritoryCommandExecution.Reject(NoHouse));

        TerritoryCommandResult Record(TerritoryCommandExecution execution)
        {
            return Record(nextCommandId++, execution);
        }

        TerritoryCommandResult Record(
            long commandId, TerritoryCommandExecution execution)
        {
            var result = new TerritoryCommandResult(
                commandId, execution.Status, execution.Reason);
            results.Add(result.CommandId, result);
            resultOrder.Enqueue(result.CommandId);
            if (resultOrder.Count > HistoryLimit)
                results.Remove(resultOrder.Dequeue());
            StatusChanged?.Invoke(result);
            return result;
        }
    }
}
