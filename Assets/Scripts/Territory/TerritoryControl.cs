using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// What one family has going for it on one block, gathered from the systems that own
    /// each number. Nothing here is stored twice: the caller reads Presence from EPIC 4,
    /// Fear from EPIC 5, compliance from EPIC 6 and Power from the response ledger, and
    /// hands them over. The control reading owns none of them.
    /// </summary>
    public readonly struct TerritoryControlInputs
    {
        public TerritoryControlInputs(
            TerritoryGangId gangId,
            float presence,
            float fear,
            float compliance,
            float power)
        {
            GangId = gangId;
            Presence = presence;
            Fear = fear;
            Compliance = compliance;
            Power = power;
        }

        public TerritoryGangId GangId { get; }

        /// <summary>Men on the ground, weighted (EPIC 4).</summary>
        public float Presence { get; }

        /// <summary>How much this street fears this family (EPIC 5).</summary>
        public float Fear { get; }

        /// <summary>The share of the block's premises paying this family (EPIC 6).</summary>
        public float Compliance { get; }

        /// <summary>Whether the family answers for what happens to what it protects -
        /// 1 is a house that always answers, below 1 is one that does not (CTRL-015).</summary>
        public float Power { get; }
    }

    /// <summary>What a family's standing on a block is made of, term by term.</summary>
    public readonly struct TerritoryControlScore
    {
        public TerritoryControlScore(
            TerritoryGangId gangId,
            float presenceTerm,
            float fearTerm,
            float complianceTerm,
            float power,
            float total)
        {
            GangId = gangId;
            PresenceTerm = presenceTerm;
            FearTerm = fearTerm;
            ComplianceTerm = complianceTerm;
            Power = power;
            Total = total;
        }

        public TerritoryGangId GangId { get; }
        public float PresenceTerm { get; }
        public float FearTerm { get; }
        public float ComplianceTerm { get; }

        /// <summary>The coefficient the three terms were scaled by.</summary>
        public float Power { get; }

        public float Total { get; }
    }

    /// <summary>
    /// Every number the control reading is made of. The weights are extensible on purpose:
    /// Power arrived after the first three, and the Phase-2 infrastructure term will arrive
    /// after Power, neither of them re-cutting the calculation.
    /// </summary>
    public sealed class TerritoryControlConfig
    {
        public TerritoryControlConfig(
            float presenceWeight = 0.35f,
            float fearWeight = 0.25f,
            float complianceWeight = 0.4f,
            float influencedAt = 12f,
            float controlledAt = 38f,
            float dominatedAt = 65f,
            float contestedMargin = 10f,
            float contestedFloor = 12f,
            float contestedExitMargin = 16f,
            int holdTicks = 2,
            float powerFloor = 0.5f,
            float powerPenalty = 0.5f,
            float powerMemoryHours = 72f,
            float powerAnswerWindowHours = 12f)
        {
            PresenceWeight = Math.Max(0f, presenceWeight);
            FearWeight = Math.Max(0f, fearWeight);
            ComplianceWeight = Math.Max(0f, complianceWeight);
            InfluencedAt = influencedAt;
            ControlledAt = Math.Max(influencedAt, controlledAt);
            DominatedAt = Math.Max(ControlledAt, dominatedAt);
            ContestedMargin = Math.Max(0f, contestedMargin);
            ContestedFloor = Math.Max(0f, contestedFloor);
            ContestedExitMargin = Math.Max(ContestedMargin, contestedExitMargin);
            HoldTicks = Math.Max(1, holdTicks);
            PowerFloor = Math.Min(1f, Math.Max(0f, powerFloor));
            PowerPenalty = Math.Max(0f, powerPenalty);
            PowerMemoryHours = Math.Max(1f, powerMemoryHours);
            PowerAnswerWindowHours = Math.Max(0.01f, powerAnswerWindowHours);
        }

        public float PresenceWeight { get; }
        public float FearWeight { get; }
        public float ComplianceWeight { get; }

        /// <summary>Below this a family is on the street, not in charge of it.</summary>
        public float InfluencedAt { get; }

        /// <summary>
        /// Held. Deliberately above what any ONE pillar can reach on its own: all the men
        /// in the city on a street where nobody fears you and no shop pays you is presence,
        /// not control, and a street that only remembers a killing is a frightened street,
        /// not a held one. Two of the three, or shops that pay - that is a street.
        /// </summary>
        public float ControlledAt { get; }
        public float DominatedAt { get; }

        /// <summary>How close the second family has to be for the block to be a fight.</summary>
        public float ContestedMargin { get; }

        /// <summary>And how much both of them have to be worth - two families squabbling
        /// over a street neither of them holds is not a contested block, it is an empty one.</summary>
        public float ContestedFloor { get; }

        /// <summary>The gap that ends a fight is wider than the one that starts it, so a
        /// block on the line does not flicker between contested and held.</summary>
        public float ContestedExitMargin { get; }

        /// <summary>How many readings running must agree before the block changes its
        /// mind. One tick over a line is a moment, not a change of hands.</summary>
        public int HoldTicks { get; }

        public float PowerFloor { get; }

        /// <summary>How far an unanswered incident drags the coefficient down.</summary>
        public float PowerPenalty { get; }

        /// <summary>How long the street remembers whether a house answered.</summary>
        public float PowerMemoryHours { get; }

        /// <summary>How long a house has to answer before the street calls it unanswered.</summary>
        public float PowerAnswerWindowHours { get; }

        public static TerritoryControlConfig Default { get; } = new TerritoryControlConfig();

        /// <summary>
        /// What one family is worth on a block: the three things it can actually do about
        /// the street, scaled by whether it answers for what happens on it.
        /// </summary>
        public TerritoryControlScore Score(TerritoryControlInputs inputs)
        {
            var presence = PresenceWeight * Math.Max(0f, inputs.Presence);
            var fear = FearWeight * Math.Max(0f, inputs.Fear);
            var compliance = ComplianceWeight * Math.Max(0f, inputs.Compliance);
            var power = inputs.Power <= 0f ? 1f : inputs.Power;
            return new TerritoryControlScore(
                inputs.GangId, presence, fear, compliance, power,
                (presence + fear + compliance) * power);
        }
    }

    /// <summary>
    /// How a block reads once every family's standing on it is known. The classification
    /// is pure: same scores, same answer, every time, and no part of it is stored on the
    /// block - there is no owner field to drift out of step with the men in the street.
    /// </summary>
    public static class TerritoryControlReading
    {
        /// <summary>
        /// Who leads, how far ahead, and what that makes the block. Contested outranks the
        /// ladder: a street two families are both worth something on is a fight, whatever
        /// the leader's number says.
        /// </summary>
        public static TerritoryControlState Read(
            IReadOnlyList<TerritoryControlScore> scores,
            TerritoryControlConfig config,
            bool alreadyContested,
            out TerritoryGangId leader,
            out float best,
            out float second)
        {
            config ??= TerritoryControlConfig.Default;
            leader = default;
            best = 0f;
            second = 0f;

            if (scores == null || scores.Count == 0)
                return TerritoryControlState.Uncontrolled;

            for (var i = 0; i < scores.Count; i++)
            {
                var total = scores[i].Total;
                if (total > best)
                {
                    second = best;
                    best = total;
                    leader = scores[i].GangId;
                }
                else if (total > second)
                {
                    second = total;
                }
            }

            if (best < config.InfluencedAt)
            {
                leader = default;
                return TerritoryControlState.Uncontrolled;
            }

            // A fight is two families both worth something, close together. The gap that
            // ends one is wider than the gap that starts it.
            var margin = alreadyContested ? config.ContestedExitMargin : config.ContestedMargin;
            if (second >= config.ContestedFloor && best - second <= margin)
                return TerritoryControlState.Contested;

            if (best >= config.DominatedAt)
                return TerritoryControlState.Dominated;
            return best >= config.ControlledAt
                ? TerritoryControlState.Controlled
                : TerritoryControlState.Influenced;
        }
    }

    /// <summary>A block whose reading moved; the runtime turns it into events.</summary>
    public readonly struct TerritoryControlChange
    {
        public TerritoryControlChange(
            TerritoryBlockId blockId,
            TerritoryControlState previous,
            TerritoryControlState current,
            TerritoryGangId previousLeader,
            TerritoryGangId leader,
            double gameHour)
        {
            BlockId = blockId;
            Previous = previous;
            Current = current;
            PreviousLeader = previousLeader;
            Leader = leader;
            GameHour = gameHour;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryControlState Previous { get; }
        public TerritoryControlState Current { get; }
        public TerritoryGangId PreviousLeader { get; }
        public TerritoryGangId Leader { get; }
        public double GameHour { get; }

        /// <summary>True when the family that held it has dropped out of holding it.</summary>
        public bool LostControl =>
            Held(Previous) && !(Held(Current) && Leader == PreviousLeader);

        public bool BecameContested =>
            Current == TerritoryControlState.Contested &&
            Previous != TerritoryControlState.Contested;

        static bool Held(TerritoryControlState state) =>
            state == TerritoryControlState.Controlled || state == TerritoryControlState.Dominated;
    }

    /// <summary>
    /// Who answers for what happens on the ground they are paid to protect.
    ///
    /// The third pillar: a house can be feared and standing on every corner and still not
    /// be POWER, if a shop it protects gets its window put in and nobody comes. Every
    /// incident against something a family protects is written down here with whether the
    /// family answered it inside the window, and what comes out is a coefficient that
    /// scales everything else that family is worth on the block.
    ///
    /// Pure and stamped in game hours; the street's own violence feeds it through the same
    /// fear acts EPIC 5 already files, rather than a second event stream of its own.
    /// </summary>
    public sealed class TerritoryPowerLedger
    {
        readonly Dictionary<TerritoryBlockId, BlockRow> blocks =
            new Dictionary<TerritoryBlockId, BlockRow>();
        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();

        public TerritoryPowerLedger(TerritoryControlConfig config = null) =>
            Config = config ?? TerritoryControlConfig.Default;

        public TerritoryControlConfig Config { get; set; }

        public IReadOnlyList<TerritoryBlockId> Blocks => blockIds;

        /// <summary>Something was done to what this family protects here.</summary>
        public void Incident(
            TerritoryBlockId blockId, TerritoryGangId gangId, double gameHour)
        {
            if (!blockId.IsValid || !gangId.IsValid)
                return;
            Row(blockId).Gang(gangId).Add(gameHour);
        }

        /// <summary>
        /// The family did something about it. Everything still inside the answer window is
        /// counted as answered - a house that hits back once for a night of trouble has
        /// answered for the night.
        /// </summary>
        public void Answered(
            TerritoryBlockId blockId, TerritoryGangId gangId, double gameHour)
        {
            if (!blocks.TryGetValue(blockId, out var row))
                return;
            row.Gang(gangId).Answer(gameHour, Config);
        }

        /// <summary>
        /// What this family's word is worth here: one when it answers for its ground,
        /// falling toward the floor as the unanswered pile up, and climbing back as the
        /// street forgets.
        /// </summary>
        public float Coefficient(
            TerritoryBlockId blockId, TerritoryGangId gangId, double gameHour)
        {
            if (!blocks.TryGetValue(blockId, out var row))
                return 1f;
            return row.Coefficient(gangId, gameHour, Config);
        }

        /// <summary>The incidents behind the number, for the inspector.</summary>
        public void Collect(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            double gameHour,
            out int incidents,
            out int unanswered)
        {
            Collect(blockId, gangId, gameHour, out incidents, out unanswered, out _, out _);
        }

        /// <summary>
        /// The same, with the HOURS behind the number (AI-001 S1): when the oldest
        /// still-unanswered incident happened, and when the latest incident of any
        /// kind did. Both are NaN when there is nothing. The mind's view carries these
        /// instead of the hour of the think, so a window can actually close.
        /// </summary>
        public void Collect(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            double gameHour,
            out int incidents,
            out int unanswered,
            out double oldestUnansweredAt,
            out double lastAt)
        {
            incidents = 0;
            unanswered = 0;
            oldestUnansweredAt = double.NaN;
            lastAt = double.NaN;
            if (blocks.TryGetValue(blockId, out var row))
                row.Count(gangId, gameHour, Config, out incidents, out unanswered,
                    out oldestUnansweredAt, out lastAt);
        }

        /// <summary>Drop what the street has forgotten, so a long campaign does not carry
        /// every window ever broken.</summary>
        public void Forget(double gameHour)
        {
            for (var i = blockIds.Count - 1; i >= 0; i--)
            {
                var blockId = blockIds[i];
                var row = blocks[blockId];
                row.Forget(gameHour, Config);
                if (!row.IsEmpty)
                    continue;
                blocks.Remove(blockId);
                blockIds.RemoveAt(i);
            }
        }

        BlockRow Row(TerritoryBlockId blockId)
        {
            if (blocks.TryGetValue(blockId, out var row))
                return row;
            row = new BlockRow();
            blocks.Add(blockId, row);
            blockIds.Add(blockId);
            return row;
        }

        sealed class BlockRow
        {
            readonly List<GangRow> gangs = new List<GangRow>();

            public bool IsEmpty => gangs.Count == 0;

            public GangRow Gang(TerritoryGangId gangId)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        return gangs[i];
                var added = new GangRow(gangId);
                gangs.Add(added);
                return added;
            }

            public float Coefficient(
                TerritoryGangId gangId, double gameHour, TerritoryControlConfig config)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        return gangs[i].Coefficient(gameHour, config);
                return 1f;
            }

            public void Count(
                TerritoryGangId gangId, double gameHour, TerritoryControlConfig config,
                out int incidents, out int unanswered, out double oldestUnansweredAt,
                out double lastAt)
            {
                incidents = 0;
                unanswered = 0;
                oldestUnansweredAt = double.NaN;
                lastAt = double.NaN;
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        gangs[i].Count(gameHour, config, out incidents, out unanswered,
                            out oldestUnansweredAt, out lastAt);
            }

            public void Forget(double gameHour, TerritoryControlConfig config)
            {
                for (var i = gangs.Count - 1; i >= 0; i--)
                {
                    gangs[i].Forget(gameHour, config);
                    if (gangs[i].IsEmpty)
                        gangs.RemoveAt(i);
                }
            }
        }

        sealed class GangRow
        {
            readonly List<Incident> incidents = new List<Incident>();

            public GangRow(TerritoryGangId gangId) => GangId = gangId;

            public TerritoryGangId GangId { get; }
            public bool IsEmpty => incidents.Count == 0;

            public void Add(double gameHour) =>
                incidents.Add(new Incident { At = gameHour, Answered = false });

            public void Answer(double gameHour, TerritoryControlConfig config)
            {
                for (var i = 0; i < incidents.Count; i++)
                {
                    var incident = incidents[i];
                    if (incident.Answered ||
                        gameHour - incident.At > config.PowerAnswerWindowHours)
                        continue;
                    incident.Answered = true;
                    incidents[i] = incident;
                }
            }

            public float Coefficient(double gameHour, TerritoryControlConfig config)
            {
                Count(gameHour, config, out var total, out var unanswered, out _, out _);
                if (total == 0)
                    return 1f;
                var share = (float)unanswered / total;
                return Math.Max(config.PowerFloor, 1f - share * config.PowerPenalty);
            }

            /// <summary>
            /// An incident still inside its answer window is not yet a failure - the house
            /// has time to come. Only what is past the window and unanswered counts against.
            /// The two hours are what a mind measures its windows from: the oldest
            /// incident nobody has answered yet (inside its window or past it), and the
            /// latest incident of any kind.
            /// </summary>
            public void Count(
                double gameHour, TerritoryControlConfig config,
                out int total, out int unanswered, out double oldestUnansweredAt,
                out double lastAt)
            {
                total = 0;
                unanswered = 0;
                oldestUnansweredAt = double.NaN;
                lastAt = double.NaN;
                for (var i = 0; i < incidents.Count; i++)
                {
                    if (gameHour - incidents[i].At > config.PowerMemoryHours)
                        continue;
                    total++;
                    if (double.IsNaN(lastAt) || incidents[i].At > lastAt)
                        lastAt = incidents[i].At;
                    if (incidents[i].Answered)
                        continue;
                    if (double.IsNaN(oldestUnansweredAt) || incidents[i].At < oldestUnansweredAt)
                        oldestUnansweredAt = incidents[i].At;
                    if (gameHour - incidents[i].At > config.PowerAnswerWindowHours)
                        unanswered++;
                }
            }

            public void Forget(double gameHour, TerritoryControlConfig config)
            {
                for (var i = incidents.Count - 1; i >= 0; i--)
                    if (gameHour - incidents[i].At > config.PowerMemoryHours)
                        incidents.RemoveAt(i);
            }

            struct Incident
            {
                public double At;
                public bool Answered;
            }
        }
    }

    /// <summary>
    /// What each block currently reads as, and the patience that keeps it from flickering.
    /// The state is DERIVED and re-derived: this holds only what a derivation cannot know
    /// on its own - what the block said last time, and how many readings running have
    /// disagreed with it.
    /// </summary>
    public sealed class TerritoryControlLedger
    {
        readonly Dictionary<TerritoryBlockId, Row> blocks = new Dictionary<TerritoryBlockId, Row>();
        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();

        public TerritoryControlLedger(TerritoryControlConfig config = null) =>
            Config = config ?? TerritoryControlConfig.Default;

        public TerritoryControlConfig Config { get; set; }

        public IReadOnlyList<TerritoryBlockId> Blocks => blockIds;

        public TerritoryControlState StateOf(TerritoryBlockId blockId) =>
            blocks.TryGetValue(blockId, out var row) ? row.State : TerritoryControlState.Unknown;

        public TerritoryGangId LeaderOf(TerritoryBlockId blockId) =>
            blocks.TryGetValue(blockId, out var row) ? row.Leader : default;

        /// <summary>
        /// Read the block from the scores now standing, and change its mind only if the
        /// same answer has come back for enough readings running.
        /// </summary>
        public bool Read(
            TerritoryBlockId blockId,
            IReadOnlyList<TerritoryControlScore> scores,
            double gameHour,
            out TerritoryControlChange change)
        {
            change = default;
            if (!blockId.IsValid)
                return false;

            var row = RowFor(blockId);
            var state = TerritoryControlReading.Read(
                scores, Config, row.State == TerritoryControlState.Contested,
                out var leader, out row.Best, out row.Second);

            if (state == row.State && leader == row.Leader)
            {
                row.Candidate = state;
                row.CandidateLeader = leader;
                row.Held = 0;
                return false;
            }

            // The same different answer, for as many readings running as the config asks,
            // before a street is said to have changed hands. A new answer starts the count
            // over rather than being refused outright - with a patience of one, the first
            // reading is the change.
            if (state != row.Candidate || leader != row.CandidateLeader)
            {
                row.Candidate = state;
                row.CandidateLeader = leader;
                row.Held = 0;
            }

            row.Held++;
            if (row.Held < Config.HoldTicks)
                return false;

            change = new TerritoryControlChange(
                blockId, row.State, state, row.Leader, leader, gameHour);
            row.State = state;
            row.Leader = leader;
            row.Held = 0;
            return true;
        }

        public void Scores(
            TerritoryBlockId blockId, out float best, out float second)
        {
            best = 0f;
            second = 0f;
            if (!blocks.TryGetValue(blockId, out var row))
                return;
            best = row.Best;
            second = row.Second;
        }

        Row RowFor(TerritoryBlockId blockId)
        {
            if (blocks.TryGetValue(blockId, out var row))
                return row;
            row = new Row();
            blocks.Add(blockId, row);
            blockIds.Add(blockId);
            return row;
        }

        sealed class Row
        {
            public TerritoryControlState State = TerritoryControlState.Unknown;
            public TerritoryGangId Leader;
            public TerritoryControlState Candidate = TerritoryControlState.Unknown;
            public TerritoryGangId CandidateLeader;
            public int Held;
            public float Best;
            public float Second;
        }
    }

    /// <summary>How a quarter reads, counted off its blocks. Never a thing that is held.</summary>
    public readonly struct TerritoryNeighborhoodStatus
    {
        public TerritoryNeighborhoodStatus(
            TerritoryNeighborhoodId neighborhoodId,
            int blocks,
            int neutral,
            int influenced,
            int contested,
            int controlled,
            int dominated,
            TerritoryGangId leader)
        {
            NeighborhoodId = neighborhoodId;
            Blocks = blocks;
            Neutral = neutral;
            Influenced = influenced;
            Contested = contested;
            Controlled = controlled;
            Dominated = dominated;
            Leader = leader;
        }

        public TerritoryNeighborhoodId NeighborhoodId { get; }
        public int Blocks { get; }
        public int Neutral { get; }
        public int Influenced { get; }
        public int Contested { get; }
        public int Controlled { get; }
        public int Dominated { get; }

        /// <summary>The family holding the most of it outright, if any one does.</summary>
        public TerritoryGangId Leader { get; }
    }

    /// <summary>
    /// A quarter's standing, counted off its blocks every time it is asked. Nothing is
    /// stored: a neighborhood is not a thing that can be taken, it is what its streets
    /// add up to.
    /// </summary>
    public static class TerritoryNeighborhoodReading
    {
        public static TerritoryNeighborhoodStatus Read(
            TerritoryNeighborhoodId neighborhoodId,
            IReadOnlyList<TerritoryBlockId> memberBlocks,
            TerritoryControlLedger control)
        {
            var neutral = 0;
            var influenced = 0;
            var contested = 0;
            var controlled = 0;
            var dominated = 0;
            var leaders = new List<TerritoryGangId>();
            var counts = new List<int>();

            for (var i = 0; memberBlocks != null && i < memberBlocks.Count; i++)
            {
                var blockId = memberBlocks[i];
                switch (control?.StateOf(blockId) ?? TerritoryControlState.Unknown)
                {
                    case TerritoryControlState.Influenced: influenced++; break;
                    case TerritoryControlState.Contested: contested++; break;
                    case TerritoryControlState.Controlled: controlled++; break;
                    case TerritoryControlState.Dominated: dominated++; break;
                    default: neutral++; break;
                }

                var leader = control?.LeaderOf(blockId) ?? default;
                if (!leader.IsValid)
                    continue;
                var at = leaders.IndexOf(leader);
                if (at < 0)
                {
                    leaders.Add(leader);
                    counts.Add(1);
                }
                else
                {
                    counts[at]++;
                }
            }

            var best = 0;
            var tied = false;
            var top = default(TerritoryGangId);
            for (var i = 0; i < leaders.Count; i++)
            {
                if (counts[i] > best)
                {
                    best = counts[i];
                    top = leaders[i];
                    tied = false;
                }
                else if (counts[i] == best)
                {
                    tied = true;
                }
            }

            return new TerritoryNeighborhoodStatus(
                neighborhoodId,
                memberBlocks?.Count ?? 0,
                neutral, influenced, contested, controlled, dominated,
                tied ? default : top);
        }
    }
}
