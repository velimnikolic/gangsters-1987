using System.Collections.Generic;
using LivingCity.Territory;

namespace RoadDemo
{
    /// <summary>
    /// THE TWO ORDERS THAT WALK A WHOLE BLOCK.
    ///
    /// SHAKE DOWN THE BLOCK sends the crew door to door through every shop on it that
    /// does not pay us yet, and puts it to each owner in turn. LEAN ON THE HOLDOUTS is
    /// the same walk against the doors that refused us or will not say yes, with a
    /// threat at each instead of a question.
    ///
    /// Both reuse the collection round's machine - the route, the arrival, the abandon -
    /// because a walk down a block's doors is a walk down a block's doors. Only what
    /// happens at the counter differs, and the round's Kind says which.
    ///
    /// The WHOLE crew walks these. A collection can spare a man and an escort (the men
    /// are carrying money, not making a point); a shakedown cannot, because the men in
    /// the doorway ARE the argument.
    ///
    /// What the rules are - which doors are worth asking, whether the men lean on the
    /// spot after a no - is <see cref="TerritoryShakedown"/>, pure and tested. This file
    /// is the scene edge and decides nothing.
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        public TerritoryCommandExecution Execute(ShakeDownBlockCommand command) =>
            WalkTheDoors(command.GroupId, command.BlockId, RoundKind.ShakeDown);

        public TerritoryCommandExecution Execute(LeanOnHoldoutsCommand command) =>
            WalkTheDoors(command.GroupId, command.BlockId, RoundKind.Lean);

        /// <summary>
        /// The one walk both orders are. The stops are chosen by the pure rule for the
        /// kind, ordered by the street the way a collection round is, and the crew is
        /// marched at the first of them.
        /// </summary>
        TerritoryCommandExecution WalkTheDoors(
            TerritoryCommandNodeId groupId, TerritoryBlockId blockId, RoundKind kind)
        {
            if (!blockId.IsValid)
                return TerritoryCommandExecution.Reject("Unknown territory block.");
            if (racket == null || geography == null)
                return TerritoryCommandExecution.Reject(
                    "The racket is not running in this scene.");

            var unit = FindPlayerUnit(groupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);

            var gang = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            var candidates = new List<RoundStop>();
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                var state = racket.StateOf(businessId, gang);
                var wanted = kind == RoundKind.Lean
                    ? TerritoryShakedown.IsHoldout(state, OursByDeed(businessId))
                    : TerritoryShakedown.WorthAsking(state, OursByDeed(businessId));
                if (!wanted)
                    continue;
                // A shop with its shutters down cannot be asked anything.
                if (!RacketCanAccrueAt(businessId, lastGameHour))
                    continue;
                if (!TryGetBusinessApproach(businessId, out var door))
                    continue;
                candidates.Add(new RoundStop(businessId, door));
            }

            if (candidates.Count == 0)
                return TerritoryCommandExecution.Reject(kind == RoundKind.Lean
                    ? LeanRefusal
                    : ShakedownRefusal);

            var round = new CollectionRound
            {
                Kind = kind,
                CrewId = unit.CrewId,
                GangId = gang,
                BlockId = blockId,
                Walkers = unit,
                Collector = CollectorOf(unit),
            };
            if (round.Collector == null)
                return TerritoryCommandExecution.Reject(
                    "The crew has nobody on his feet to put at a door.");
            OrderStops(candidates, UnitAnchor(unit), round.Stops);

            // One errand at a time, the same rule a round keeps.
            DropPendingApproaches(unit.CrewId);
            rounds.Add(round);
            BumpRacketSeam();

            if (!crews.MarchTo(unit, round.Stops[0].Door))
            {
                rounds.Remove(round);
                return TerritoryCommandExecution.Reject(
                    "The physical crew refused the walk.");
            }

            return TerritoryCommandExecution.Pending(kind == RoundKind.Lean
                ? "The men are walking the holdouts."
                : "The men are walking the block, door to door.");
        }

        /// <summary>The refusals, named once. The block file's keys print these WITHOUT
        /// firing the order (BlockRacketSeam.Refusal), so a key that says it cannot fire
        /// and a command that refuses can never disagree about why.</summary>
        internal const string ShakedownRefusal = "every door here has answered us";
        internal const string LeanRefusal = "nobody is holding out";

        /// <summary>Whether the family holds this door's deed - our own shop, our front,
        /// the headquarters. The one place the two block orders ask it, so the key that
        /// offers the order and the order itself count the same doors.</summary>
        internal static bool OursByDeed(TerritoryBusinessId businessId) =>
            LivingCity.Business.BusinessDeeds.GangOf(businessId) ==
            LivingCity.Gangs.GangCatalog.PlayerGangId;

        /// <summary>
        /// What happens at ONE door, whichever walk brought the men to it. A collection
        /// settles money; a shakedown puts the question and, on a no, may put hands on
        /// the door as well - the crew's policy decides that and nothing here does.
        /// </summary>
        void SettleDoor(
            CollectionRound round, RoundStop stop, DemoCrews.Unit unit,
            TerritoryActorObservation observation, double gameHour)
        {
            if (round.Kind == RoundKind.Collect)
            {
                SettleStop(round, stop, unit, observation, gameHour);
                return;
            }

            var businessId = stop.BusinessId;
            var gang = round.GangId;
            var mouth = observation.CharacterId;

            if (round.Kind == RoundKind.Lean)
            {
                ResolveThreat(gang, businessId, mouth, out var threatened, out _);
                AnnounceVerdict(businessId, true, threatened, mouth);
                return;
            }

            // THE ASK. The state moves without a slip of its own: the men arriving and
            // the owner's answer are one visit, and the answer is the news (Part A).
            racketChanges.Clear();
            racket.Approach(businessId, gang, gameHour, racketChanges, announce: false);

            ResolveDemand(gang, businessId, out var verdict, out _);
            AnnounceVerdict(businessId, false, verdict, mouth);

            PolicyAndArchetype(unit, out var policyLevel, out _);
            if (TerritoryShakedown.ThreatenAfter(verdict, policyLevel))
            {
                ResolveThreat(gang, businessId, mouth, out var after, out _);
                AnnounceVerdict(businessId, true, after, mouth);
            }
        }
    }
}
