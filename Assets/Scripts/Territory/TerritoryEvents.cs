using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    public interface ITerritoryEvent
    {
        TerritoryBlockId BlockId { get; }
        double GameHour { get; }
    }

    public readonly struct ActorEnteredBlock : ITerritoryEvent
    {
        public ActorEnteredBlock(
            TerritoryBlockId blockId,
            TerritoryCharacterId actorId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId groupId,
            double gameHour)
        {
            BlockId = blockId;
            ActorId = actorId;
            GangId = gangId;
            GroupId = groupId;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryCharacterId ActorId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCommandNodeId GroupId { get; }
        public double GameHour { get; }
    }

    public readonly struct ActorLeftBlock : ITerritoryEvent
    {
        public ActorLeftBlock(
            TerritoryBlockId blockId,
            TerritoryCharacterId actorId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId groupId,
            double gameHour)
        {
            BlockId = blockId;
            ActorId = actorId;
            GangId = gangId;
            GroupId = groupId;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryCharacterId ActorId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCommandNodeId GroupId { get; }
        public double GameHour { get; }
    }

    public readonly struct PresenceChanged : ITerritoryEvent
    {
        public PresenceChanged(
            TerritoryBlockId blockId, TerritoryGangId gangId,
            float previous, float current, double gameHour)
        {
            BlockId = blockId;
            GangId = gangId;
            Previous = previous;
            Current = current;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId GangId { get; }
        public float Previous { get; }
        public float Current { get; }
        public double GameHour { get; }
    }

    public readonly struct FearEventRecorded : ITerritoryEvent
    {
        public FearEventRecorded(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            TerritoryCharacterId sourceActorId,
            float amount,
            double gameHour)
        {
            BlockId = blockId;
            GangId = gangId;
            SourceActorId = sourceActorId;
            Amount = amount;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCharacterId SourceActorId { get; }
        public float Amount { get; }
        public double GameHour { get; }
    }

    public readonly struct FearChanged : ITerritoryEvent
    {
        public FearChanged(
            TerritoryBlockId blockId, float previous, float current, double gameHour)
        {
            BlockId = blockId;
            Previous = previous;
            Current = current;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public float Previous { get; }
        public float Current { get; }
        public double GameHour { get; }
    }

    public readonly struct BusinessComplianceChanged : ITerritoryEvent
    {
        public BusinessComplianceChanged(
            TerritoryBlockId blockId,
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            float previous,
            float current,
            double gameHour)
        {
            BlockId = blockId;
            BusinessId = businessId;
            GangId = gangId;
            Previous = previous;
            Current = current;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryBusinessId BusinessId { get; }
        public TerritoryGangId GangId { get; }
        public float Previous { get; }
        public float Current { get; }
        public double GameHour { get; }
    }

    public readonly struct BlockControlChanged : ITerritoryEvent
    {
        public BlockControlChanged(
            TerritoryBlockId blockId,
            TerritoryGangId previousLeader,
            TerritoryGangId currentLeader,
            TerritoryControlState current,
            double gameHour)
        {
            BlockId = blockId;
            PreviousLeader = previousLeader;
            CurrentLeader = currentLeader;
            Current = current;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId PreviousLeader { get; }
        public TerritoryGangId CurrentLeader { get; }
        public TerritoryControlState Current { get; }
        public double GameHour { get; }
    }

    public readonly struct BlockBecameContested : ITerritoryEvent
    {
        public BlockBecameContested(
            TerritoryBlockId blockId,
            TerritoryGangId firstGangId,
            TerritoryGangId secondGangId,
            double gameHour)
        {
            BlockId = blockId;
            FirstGangId = firstGangId;
            SecondGangId = secondGangId;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId FirstGangId { get; }
        public TerritoryGangId SecondGangId { get; }
        public double GameHour { get; }
    }

    public readonly struct BlockControlLost : ITerritoryEvent
    {
        public BlockControlLost(
            TerritoryBlockId blockId, TerritoryGangId gangId, double gameHour)
        {
            BlockId = blockId;
            GangId = gangId;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId GangId { get; }
        public double GameHour { get; }
    }

    public readonly struct TerritoryEventRecord
    {
        public TerritoryEventRecord(long sequence, ITerritoryEvent value)
        {
            Sequence = sequence;
            Value = value;
        }

        public long Sequence { get; }
        public ITerritoryEvent Value { get; }
    }

    /// <summary>
    /// Small typed event surface for territory only. It announces changes and keeps a
    /// bounded debug history; authoritative values remain queryable from TerritorySimulationState.
    /// </summary>
    public sealed class TerritoryEventStream
    {
        const int HistoryLimit = 128;

        readonly List<TerritoryEventRecord> history = new List<TerritoryEventRecord>();
        long nextSequence = 1;

        public event Action<ActorEnteredBlock> ActorEntered;
        public event Action<ActorLeftBlock> ActorLeft;
        public event Action<PresenceChanged> Presence;
        public event Action<FearEventRecorded> FearRecorded;
        public event Action<FearChanged> Fear;
        public event Action<BusinessComplianceChanged> BusinessCompliance;
        public event Action<BlockControlChanged> BlockControl;
        public event Action<BlockBecameContested> BlockContested;
        public event Action<BlockControlLost> ControlLost;
        public event Action<ITerritoryEvent> Published;

        public IReadOnlyList<TerritoryEventRecord> Recent => history;

        internal void Publish(ActorEnteredBlock value) => Record(value, ActorEntered);
        internal void Publish(ActorLeftBlock value) => Record(value, ActorLeft);
        internal void Publish(PresenceChanged value) => Record(value, Presence);
        internal void Publish(FearEventRecorded value) => Record(value, FearRecorded);
        internal void Publish(FearChanged value) => Record(value, Fear);
        internal void Publish(BusinessComplianceChanged value) =>
            Record(value, BusinessCompliance);
        internal void Publish(BlockControlChanged value) => Record(value, BlockControl);
        internal void Publish(BlockBecameContested value) => Record(value, BlockContested);
        internal void Publish(BlockControlLost value) => Record(value, ControlLost);

        void Record<T>(T value, Action<T> typed) where T : struct, ITerritoryEvent
        {
            if (history.Count == HistoryLimit)
                history.RemoveAt(0);

            history.Add(new TerritoryEventRecord(nextSequence++, value));
            typed?.Invoke(value);
            Published?.Invoke(value);
        }
    }
}
