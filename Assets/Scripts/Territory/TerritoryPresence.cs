using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// What a body was DOING on the block when it was sampled, read off the physical
    /// truth the project already keeps (a man's walk mode, whether his crew is in a car).
    /// A crew driving through a block is not holding it, and the weights say so.
    /// </summary>
    public enum TerritoryActorActivity
    {
        Unknown = 0,

        /// <summary>Passing through - riding, fleeing, being carried somewhere else.</summary>
        Transit,

        /// <summary>On foot and going somewhere: walking, homing, striding.</summary>
        Moving,

        /// <summary>Standing on the ground or working on it - posted, or in a fight for it.</summary>
        Stationed,
    }

    /// <summary>
    /// The rank a sampled body carries, adapted from real personnel identity. It is the
    /// PERSON who is present; command responsibility for a block is a different thing
    /// entirely and contributes nothing here.
    /// </summary>
    public enum TerritoryRank
    {
        Unknown = 0,
        Hood,
        Lieutenant,
        Boss,
    }

    /// <summary>
    /// Every number Presence is made of, in one replaceable object. Nothing in the
    /// accumulation reads a constant that is not on this config, so what a man standing
    /// on a corner is worth can be retuned without touching a rule.
    /// </summary>
    public sealed class TerritoryPresenceConfig
    {
        public TerritoryPresenceConfig(
            float pointsPerContributor = 10f,
            float hoodWeight = 1f,
            float lieutenantWeight = 2f,
            float bossWeight = 3f,
            float transitWeight = 0.2f,
            float movingWeight = 0.6f,
            float stationedWeight = 1f,
            float presenceCap = 100f,
            float residualDepositPerHour = 0.5f,
            float residualCap = 30f,
            float residualHalfLifeHours = 6f)
        {
            PointsPerContributor = Math.Max(0f, pointsPerContributor);
            HoodWeight = Math.Max(0f, hoodWeight);
            LieutenantWeight = Math.Max(0f, lieutenantWeight);
            BossWeight = Math.Max(0f, bossWeight);
            TransitWeight = Math.Max(0f, transitWeight);
            MovingWeight = Math.Max(0f, movingWeight);
            StationedWeight = Math.Max(0f, stationedWeight);
            PresenceCap = Math.Max(1f, presenceCap);
            ResidualDepositPerHour = Math.Max(0f, residualDepositPerHour);
            ResidualCap = Math.Max(0f, residualCap);
            ResidualHalfLifeHours = Math.Max(0.01f, residualHalfLifeHours);
        }

        /// <summary>What one ordinary body standing on the block is worth.</summary>
        public float PointsPerContributor { get; }

        public float HoodWeight { get; }
        public float LieutenantWeight { get; }
        public float BossWeight { get; }
        public float TransitWeight { get; }
        public float MovingWeight { get; }
        public float StationedWeight { get; }

        /// <summary>The ceiling on total Presence, so the scale a label is read off is
        /// bounded however many men are put on one block.</summary>
        public float PresenceCap { get; }

        /// <summary>How much of the current physical Presence a block remembers per game
        /// hour of continued operation (PRES-006).</summary>
        public float ResidualDepositPerHour { get; }

        public float ResidualCap { get; }

        /// <summary>Game hours for the remembered half of a residual to fade (PRES-007).</summary>
        public float ResidualHalfLifeHours { get; }

        public static TerritoryPresenceConfig Default { get; } = new TerritoryPresenceConfig();

        public float RankWeight(TerritoryRank rank)
        {
            switch (rank)
            {
                case TerritoryRank.Boss: return BossWeight;
                case TerritoryRank.Lieutenant: return LieutenantWeight;
                default: return HoodWeight;
            }
        }

        public float ActivityWeight(TerritoryActorActivity activity)
        {
            switch (activity)
            {
                case TerritoryActorActivity.Transit: return TransitWeight;
                case TerritoryActorActivity.Moving: return MovingWeight;
                case TerritoryActorActivity.Stationed: return StationedWeight;
                default: return MovingWeight;
            }
        }

        /// <summary>What this one body, doing this one thing, is worth on the block.</summary>
        public float ContributionOf(TerritoryRank rank, TerritoryActorActivity activity) =>
            PointsPerContributor * RankWeight(rank) * ActivityWeight(activity);
    }

    /// <summary>One family's Presence on one block, split the way it was built.</summary>
    public readonly struct TerritoryGangPresence
    {
        public TerritoryGangPresence(
            TerritoryGangId gangId, float physical, float residual, float total)
        {
            GangId = gangId;
            Physical = physical;
            Residual = residual;
            Total = total;
        }

        public TerritoryGangId GangId { get; }

        /// <summary>What is standing there right now. Recomputed every sample, never decayed.</summary>
        public float Physical { get; }

        /// <summary>What the block remembers of recent operation. Decays.</summary>
        public float Residual { get; }

        /// <summary>Physical + residual, capped. This is the published Presence.</summary>
        public float Total { get; }
    }

    /// <summary>One body counted into a block's Presence, kept for the debug inspector.</summary>
    public readonly struct TerritoryPresenceContributor
    {
        public TerritoryPresenceContributor(
            TerritoryCharacterId characterId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId groupId,
            string displayName,
            TerritoryRank rank,
            TerritoryActorActivity activity,
            float contribution)
        {
            CharacterId = characterId;
            GangId = gangId;
            GroupId = groupId;
            DisplayName = displayName ?? "";
            Rank = rank;
            Activity = activity;
            Contribution = contribution;
        }

        public TerritoryCharacterId CharacterId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCommandNodeId GroupId { get; }
        public string DisplayName { get; }
        public TerritoryRank Rank { get; }
        public TerritoryActorActivity Activity { get; }
        public float Contribution { get; }
    }

    /// <summary>A Gang×Block total that moved on this tick; the runtime turns it into an event.</summary>
    public readonly struct TerritoryPresenceChange
    {
        public TerritoryPresenceChange(
            TerritoryBlockId blockId, TerritoryGangId gangId, float previous, float current)
        {
            BlockId = blockId;
            GangId = gangId;
            Previous = previous;
            Current = current;
        }

        public TerritoryBlockId BlockId { get; }
        public TerritoryGangId GangId { get; }
        public float Previous { get; }
        public float Current { get; }
    }

    /// <summary>
    /// Physical Presence per family per block: who is really standing there, weighted by
    /// what they are and what they are doing, plus what the block still remembers of them.
    ///
    /// It is pure and owns no time of its own: the runtime samples bodies into it on the
    /// PhysicalPresence tick and decays it on the ResidualPresence tick, both in game
    /// hours off the territory scheduler, so it is frame-rate independent and pauses when
    /// the game clock pauses. It never reads a GameObject, never assigns control, and
    /// never branches on which family it is counting - the player's men and a rival's are
    /// run through exactly the same arithmetic (PRES-008).
    /// </summary>
    public sealed class TerritoryPresenceLedger
    {
        const float Epsilon = 0.0001f;

        readonly Dictionary<TerritoryBlockId, BlockPresence> blocks =
            new Dictionary<TerritoryBlockId, BlockPresence>();
        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();
        readonly HashSet<ActorKey> sampled = new HashSet<ActorKey>();
        readonly List<TerritoryBlockId> pruned = new List<TerritoryBlockId>();
        readonly List<TerritoryGangPresence> gangScratch = new List<TerritoryGangPresence>();

        public TerritoryPresenceLedger(TerritoryPresenceConfig config = null) =>
            Config = config ?? TerritoryPresenceConfig.Default;

        public TerritoryPresenceConfig Config { get; set; }

        /// <summary>Every block carrying any Presence at all. Blocks nobody has touched
        /// are not stored, so a city of thousands of blocks costs what is being worked.</summary>
        public IReadOnlyList<TerritoryBlockId> Blocks => blockIds;

        /// <summary>Open a sample. Contributions accumulate until <see cref="CommitSample"/>.</summary>
        public void BeginSample()
        {
            sampled.Clear();
            for (var i = 0; i < blockIds.Count; i++)
                blocks[blockIds[i]].BeginSample();
        }

        /// <summary>
        /// Count one body onto one block. The dedupe unit is the CHARACTER (PRES-004):
        /// the same man seen twice in a sample - as himself and again through his crew's
        /// projection - contributes exactly once, and a leader is one body, not a
        /// multiplier over the men he leads.
        /// </summary>
        public bool Contribute(
            TerritoryBlockId blockId, TerritoryActorObservation actor, float scale = 1f)
        {
            if (!blockId.IsValid || !actor.GangId.IsValid)
                return false;
            if (!sampled.Add(new ActorKey(actor.GangId, actor.CharacterId)))
                return false;

            if (!blocks.TryGetValue(blockId, out var block))
            {
                block = new BlockPresence();
                blocks.Add(blockId, block);
                blockIds.Add(blockId);
            }

            // The scale is what the ground itself costs a family - a block the police are
            // watching is harder to hold (FEAR-013). It weights the body; it never
            // decides whether the body is there.
            var contribution =
                Config.ContributionOf(actor.Rank, actor.Activity) * Math.Max(0f, scale);
            block.Add(actor, contribution);
            return true;
        }

        /// <summary>
        /// Close the sample: physical Presence becomes exactly what was counted (a family
        /// that put nobody on the block this tick is at zero physical, whatever it was
        /// worth a minute ago), and continued operation deposits into the block's memory.
        /// </summary>
        public void CommitSample(
            double cadenceHours, List<TerritoryPresenceChange> changes = null)
        {
            var deposit = (float)Math.Max(0.0, cadenceHours) * Config.ResidualDepositPerHour;
            pruned.Clear();

            for (var i = 0; i < blockIds.Count; i++)
            {
                var blockId = blockIds[i];
                var block = blocks[blockId];
                block.CommitSample(deposit, Config, blockId, changes);
                if (block.IsEmpty)
                    pruned.Add(blockId);
            }

            Prune();
        }

        /// <summary>
        /// Fade what the block remembers. Exponential in GAME hours off the scheduler, so
        /// one long frame and a hundred short ones fade the same amount and a paused clock
        /// fades nothing. What is physically standing there is not touched: it is measured
        /// again at every sample and has nothing to decay.
        /// </summary>
        public void DecayResidual(
            double elapsedGameHours, List<TerritoryPresenceChange> changes = null)
        {
            if (elapsedGameHours <= 0.0)
                return;

            var keep = (float)Math.Pow(0.5, elapsedGameHours / Config.ResidualHalfLifeHours);
            pruned.Clear();

            for (var i = 0; i < blockIds.Count; i++)
            {
                var blockId = blockIds[i];
                var block = blocks[blockId];
                block.Decay(keep, Config, blockId, changes);
                if (block.IsEmpty)
                    pruned.Add(blockId);
            }

            Prune();
        }

        public float TotalOf(TerritoryBlockId blockId, TerritoryGangId gangId) =>
            blocks.TryGetValue(blockId, out var block) ? block.TotalOf(gangId) : 0f;

        public float PhysicalOf(TerritoryBlockId blockId, TerritoryGangId gangId) =>
            blocks.TryGetValue(blockId, out var block) ? block.PhysicalOf(gangId) : 0f;

        public float ResidualOf(TerritoryBlockId blockId, TerritoryGangId gangId) =>
            blocks.TryGetValue(blockId, out var block) ? block.ResidualOf(gangId) : 0f;

        /// <summary>Every family with any Presence on the block, ascending by gang id so
        /// two identical situations always publish an identical list.</summary>
        public void CollectGangs(TerritoryBlockId blockId, List<TerritoryGangPresence> into)
        {
            into?.Clear();
            if (into != null && blocks.TryGetValue(blockId, out var block))
                block.CollectGangs(into);
        }

        /// <summary>The bodies behind a family's physical Presence on the block, as of the
        /// last sample. The debug inspector reconciles the total against these.</summary>
        public void CollectContributors(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            List<TerritoryPresenceContributor> into)
        {
            into?.Clear();
            if (into != null && blocks.TryGetValue(blockId, out var block))
                block.CollectContributors(gangId, into);
        }

        /// <summary>The block's Presence as territory signals, merged onto what the block
        /// already says: control and each family's deed share belong to the control pass
        /// and are carried through untouched.</summary>
        public TerritoryBlockSignals Signals(
            TerritoryBlockId blockId,
            TerritoryBlockSignals previous,
            List<TerritoryGangSignals> scratch)
        {
            CollectGangs(blockId, gangScratch);
            return TerritoryPresenceSignals.Merge(previous, gangScratch, scratch);
        }

        void Prune()
        {
            for (var i = 0; i < pruned.Count; i++)
            {
                blocks.Remove(pruned[i]);
                blockIds.Remove(pruned[i]);
            }
            pruned.Clear();
        }

        sealed class BlockPresence
        {
            readonly List<GangEntry> gangs = new List<GangEntry>();

            public bool IsEmpty => gangs.Count == 0;

            public void BeginSample()
            {
                for (var i = 0; i < gangs.Count; i++)
                    gangs[i].BeginSample();
            }

            public void Add(TerritoryActorObservation actor, float contribution)
            {
                Entry(actor.GangId).Add(actor, contribution);
            }

            public void CommitSample(
                float depositRate,
                TerritoryPresenceConfig config,
                TerritoryBlockId blockId,
                List<TerritoryPresenceChange> changes)
            {
                for (var i = gangs.Count - 1; i >= 0; i--)
                {
                    var entry = gangs[i];
                    var previous = entry.Total;
                    entry.CommitSample(depositRate, config);
                    Announce(blockId, entry, previous, changes);
                    if (entry.IsEmpty)
                        gangs.RemoveAt(i);
                }
            }

            public void Decay(
                float keep,
                TerritoryPresenceConfig config,
                TerritoryBlockId blockId,
                List<TerritoryPresenceChange> changes)
            {
                for (var i = gangs.Count - 1; i >= 0; i--)
                {
                    var entry = gangs[i];
                    var previous = entry.Total;
                    entry.Decay(keep, config);
                    Announce(blockId, entry, previous, changes);
                    if (entry.IsEmpty)
                        gangs.RemoveAt(i);
                }
            }

            public float TotalOf(TerritoryGangId gangId) => Find(gangId)?.Total ?? 0f;
            public float PhysicalOf(TerritoryGangId gangId) => Find(gangId)?.Physical ?? 0f;
            public float ResidualOf(TerritoryGangId gangId) => Find(gangId)?.Residual ?? 0f;

            public void CollectGangs(List<TerritoryGangPresence> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    var entry = gangs[i];
                    into.Add(new TerritoryGangPresence(
                        entry.GangId, entry.Physical, entry.Residual, entry.Total));
                }
            }

            public void CollectContributors(
                TerritoryGangId gangId, List<TerritoryPresenceContributor> into)
            {
                Find(gangId)?.CollectContributors(into);
            }

            static void Announce(
                TerritoryBlockId blockId,
                GangEntry entry,
                float previous,
                List<TerritoryPresenceChange> changes)
            {
                if (changes == null || Math.Abs(entry.Total - previous) < Epsilon)
                    return;
                changes.Add(new TerritoryPresenceChange(
                    blockId, entry.GangId, previous, entry.Total));
            }

            GangEntry Find(TerritoryGangId gangId)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        return gangs[i];
                return null;
            }

            /// <summary>Ascending by gang id, so the published order never depends on who
            /// happened to walk onto the block first.</summary>
            GangEntry Entry(TerritoryGangId gangId)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    if (gangs[i].GangId == gangId)
                        return gangs[i];
                    if (gangs[i].GangId.Value > gangId.Value)
                    {
                        var inserted = new GangEntry(gangId);
                        gangs.Insert(i, inserted);
                        return inserted;
                    }
                }

                var appended = new GangEntry(gangId);
                gangs.Add(appended);
                return appended;
            }
        }

        sealed class GangEntry
        {
            readonly List<TerritoryPresenceContributor> contributors =
                new List<TerritoryPresenceContributor>();
            readonly List<TerritoryPresenceContributor> pending =
                new List<TerritoryPresenceContributor>();
            float pendingPhysical;

            public GangEntry(TerritoryGangId gangId) => GangId = gangId;

            public TerritoryGangId GangId { get; }
            public float Physical { get; private set; }
            public float Residual { get; private set; }
            public float Total { get; private set; }
            public bool IsEmpty => Total < Epsilon && Physical < Epsilon && Residual < Epsilon;

            public void BeginSample()
            {
                pending.Clear();
                pendingPhysical = 0f;
            }

            public void Add(TerritoryActorObservation actor, float contribution)
            {
                pendingPhysical += contribution;
                pending.Add(new TerritoryPresenceContributor(
                    actor.CharacterId, actor.GangId, actor.GroupId, actor.DisplayName,
                    actor.Rank, actor.Activity, contribution));
            }

            public void CommitSample(float depositRate, TerritoryPresenceConfig config)
            {
                Physical = Math.Min(pendingPhysical, config.PresenceCap);
                contributors.Clear();
                contributors.AddRange(pending);
                pending.Clear();
                pendingPhysical = 0f;

                if (Physical > 0f && depositRate > 0f)
                    Residual = Math.Min(config.ResidualCap, Residual + Physical * depositRate);

                Recompute(config);
            }

            public void Decay(float keep, TerritoryPresenceConfig config)
            {
                Residual *= keep;
                if (Residual < Epsilon)
                    Residual = 0f;
                Recompute(config);
            }

            public void CollectContributors(List<TerritoryPresenceContributor> into) =>
                into.AddRange(contributors);

            void Recompute(TerritoryPresenceConfig config)
            {
                Total = Math.Min(config.PresenceCap, Physical + Residual);
                if (Total < Epsilon)
                {
                    Total = 0f;
                    contributors.Clear();
                }
            }
        }

        readonly struct ActorKey : IEquatable<ActorKey>
        {
            readonly TerritoryGangId gangId;
            readonly TerritoryCharacterId characterId;

            public ActorKey(TerritoryGangId gangId, TerritoryCharacterId characterId)
            {
                this.gangId = gangId;
                this.characterId = characterId;
            }

            public bool Equals(ActorKey other) =>
                gangId == other.gangId && characterId == other.characterId;

            public override bool Equals(object obj) => obj is ActorKey other && Equals(other);
            public override int GetHashCode() =>
                gangId.GetHashCode() * 397 ^ characterId.GetHashCode();
        }
    }

    /// <summary>Which of a family's three numbers a writer is publishing.</summary>
    public enum TerritorySignalChannel
    {
        Presence,
        Influence,
        Fear,
    }

    /// <summary>One family's new value on one channel, for the merge.</summary>
    public readonly struct TerritoryGangValue
    {
        public TerritoryGangValue(TerritoryGangId gangId, float value)
        {
            GangId = gangId;
            Value = value;
        }

        public TerritoryGangId GangId { get; }
        public float Value { get; }
    }

    /// <summary>
    /// The one place a block's per-family signals are stitched together. Presence, the
    /// deed share and Fear have three separate owners - the Presence ledger, the control
    /// derivation and the Fear ledger - and none of them may wipe another's number on its
    /// way past, so every writer merges through here, one channel at a time, and they all
    /// publish the same ascending-by-gang order.
    /// </summary>
    public static class TerritoryPresenceSignals
    {
        const float Epsilon = 0.0001f;

        /// <summary>Publish the Presence channel onto what the block already says.</summary>
        public static TerritoryBlockSignals Merge(
            TerritoryBlockSignals previous,
            IReadOnlyList<TerritoryGangPresence> presence,
            List<TerritoryGangSignals> scratch)
        {
            var values = ValueScratch;
            values.Clear();
            if (presence != null)
                for (var i = 0; i < presence.Count; i++)
                    values.Add(new TerritoryGangValue(presence[i].GangId, presence[i].Total));
            return Merge(previous, values, TerritorySignalChannel.Presence, scratch);
        }

        public static TerritoryBlockSignals Merge(
            TerritoryBlockSignals previous,
            IReadOnlyList<TerritoryGangValue> updates,
            TerritorySignalChannel channel,
            List<TerritoryGangSignals> scratch)
        {
            previous ??= TerritoryBlockSignals.Empty;
            scratch ??= new List<TerritoryGangSignals>();
            Merge(previous.Gangs, updates, channel, scratch);
            return new TerritoryBlockSignals(
                previous.LocalFear,
                previous.BusinessCompliance,
                previous.CompliantBusinesses,
                previous.TotalBusinesses,
                previous.Control,
                previous.LeadingGangId,
                scratch);
        }

        /// <summary>
        /// One ascending pass over two ascending lists: what the block already says about
        /// each family, and one channel's new values. A family in one list and not the
        /// other keeps the numbers it has; a family left with nothing on any channel is
        /// not written at all. Allocation-free, because the control pass runs it over the
        /// whole city every quarter hour.
        /// </summary>
        public static void Merge(
            IReadOnlyList<TerritoryGangSignals> previous,
            IReadOnlyList<TerritoryGangValue> updates,
            TerritorySignalChannel channel,
            List<TerritoryGangSignals> into)
        {
            if (into == null)
                return;
            into.Clear();

            var carried = previous ?? Array.Empty<TerritoryGangSignals>();
            var i = 0;
            var j = 0;
            while (i < carried.Count || (updates != null && j < updates.Count))
            {
                var hasCarried = i < carried.Count;
                var hasNew = updates != null && j < updates.Count;
                int gangValue;
                if (!hasNew)
                    gangValue = carried[i].GangId.Value;
                else if (!hasCarried)
                    gangValue = updates[j].GangId.Value;
                else
                    gangValue = Math.Min(carried[i].GangId.Value, updates[j].GangId.Value);

                var gangId = new TerritoryGangId(gangValue);
                var presence = 0f;
                var influence = 0f;
                var fear = 0f;

                if (hasCarried && carried[i].GangId.Value == gangValue)
                {
                    presence = carried[i].Presence;
                    influence = carried[i].Influence;
                    fear = carried[i].Fear;
                    i++;
                }

                if (hasNew && updates[j].GangId.Value == gangValue)
                {
                    var value = updates[j].Value;
                    switch (channel)
                    {
                        case TerritorySignalChannel.Influence: influence = value; break;
                        case TerritorySignalChannel.Fear: fear = value; break;
                        default: presence = value; break;
                    }
                    j++;
                }
                else
                {
                    // Not in this pass's list: the channel is silent for this family, and
                    // silence on a channel its owner is publishing means zero.
                    switch (channel)
                    {
                        case TerritorySignalChannel.Influence: influence = 0f; break;
                        case TerritorySignalChannel.Fear: fear = 0f; break;
                        default: presence = 0f; break;
                    }
                }

                // A family left with nothing on any channel is dropped rather than
                // written as three zeroes on every block it ever walked through.
                if (Math.Abs(presence) < Epsilon && Math.Abs(influence) < Epsilon &&
                    Math.Abs(fear) < Epsilon)
                    continue;

                into.Add(new TerritoryGangSignals(gangId, presence, influence, fear));
            }
        }

        [ThreadStatic] static List<TerritoryGangValue> valueScratch;
        static List<TerritoryGangValue> ValueScratch =>
            valueScratch ??= new List<TerritoryGangValue>();
    }
}
