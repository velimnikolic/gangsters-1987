using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// What was done. Fear is earned by acts, and every act the Phase-1 street can
    /// actually produce has a name here - nothing is filed under a general "violence".
    /// </summary>
    public enum TerritoryFearCategory
    {
        Threat,
        Assault,
        PropertyDamage,
        Shot,
        Killing,
        SuccessfulRetaliation,
        IgnoredDefiance,
    }

    /// <summary>
    /// Who saw it. A killing in a back yard at four in the morning is not the killing
    /// that happened on the pavement outside a busy shop, and the street's memory of the
    /// two is not the same size.
    /// </summary>
    public enum TerritoryFearVisibility
    {
        Hidden,
        Seen,
        Public,
    }

    /// <summary>
    /// One act, as territory sees it. Immutable, and an INPUT: it is history and a
    /// trigger, never the authority on what a block feels. It assigns no control, and a
    /// gang id that is not valid means the street does not know who did it - which stays
    /// unknown rather than being credited to somebody plausible.
    /// </summary>
    public readonly struct TerritoryFearEvent
    {
        public TerritoryFearEvent(
            TerritoryGangId gangId,
            TerritoryBlockId blockId,
            TerritoryFearCategory category,
            float severity,
            TerritoryFearVisibility visibility,
            double gameHour,
            TerritoryBusinessId businessId = default,
            TerritoryCharacterId sourceActorId = default,
            TerritoryCharacterId targetActorId = default)
        {
            GangId = gangId;
            BlockId = blockId;
            Category = category;
            Severity = severity;
            Visibility = visibility;
            GameHour = gameHour;
            BusinessId = businessId;
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
        }

        public TerritoryGangId GangId { get; }
        public TerritoryBlockId BlockId { get; }
        public TerritoryFearCategory Category { get; }

        /// <summary>How much of this act there was - one shot or a magazine, a shove or a
        /// beating. Multiplies the category's impact; 1 is the ordinary case.</summary>
        public float Severity { get; }

        public TerritoryFearVisibility Visibility { get; }
        public double GameHour { get; }

        /// <summary>The premise it happened at, when it happened at one.</summary>
        public TerritoryBusinessId BusinessId { get; }

        public TerritoryCharacterId SourceActorId { get; }
        public TerritoryCharacterId TargetActorId { get; }

        /// <summary>Whether the street can name a house for this. Unattributed acts still
        /// frighten the block and still bring the police - they just make nobody feared.</summary>
        public bool IsAttributed => GangId.IsValid;
    }

    /// <summary>What one category of act is worth, and how long the street remembers it.</summary>
    public readonly struct TerritoryFearImpact
    {
        public TerritoryFearImpact(
            float impact, float memoryHalfLifeHours, float policeWeight)
        {
            Impact = impact;
            MemoryHalfLifeHours = Math.Max(0.01f, memoryHalfLifeHours);
            PoliceWeight = Math.Max(0f, policeWeight);
        }

        /// <summary>Fear points a plain, publicly seen instance of this act is worth.
        /// Negative for acts that cost a house its name.</summary>
        public float Impact { get; }

        /// <summary>Game hours for half of the memory of it to fade.</summary>
        public float MemoryHalfLifeHours { get; }

        /// <summary>How much police attention the act draws, per point of impact.</summary>
        public float PoliceWeight { get; }
    }

    /// <summary>
    /// Every number Fear is made of: what each act is worth, how long it is remembered,
    /// how much of an incident at one premise the rest of the block feels, and what
    /// violence costs in police attention. One home for the tuning - no handler may hold
    /// a constant of its own.
    /// </summary>
    public sealed class TerritoryFearConfig
    {
        readonly TerritoryFearImpact[] table;

        public TerritoryFearConfig(
            IReadOnlyDictionary<TerritoryFearCategory, TerritoryFearImpact> overrides = null,
            float hiddenWeight = 0.35f,
            float seenWeight = 0.7f,
            float publicWeight = 1f,
            float propagationFraction = 0.35f,
            float fearCap = 100f,
            float defianceWindowHours = 12f,
            float policeAttentionCap = 100f,
            float policeAttentionHalfLifeHours = 8f,
            float policeEscalation = 0.5f,
            float presenceFloor = 0.25f,
            int memoryEntriesPerGang = 24,
            float maxSeverity = 4f)
        {
            HiddenWeight = Math.Max(0f, hiddenWeight);
            SeenWeight = Math.Max(0f, seenWeight);
            PublicWeight = Math.Max(0f, publicWeight);
            PropagationFraction = Math.Min(1f, Math.Max(0f, propagationFraction));
            FearCap = Math.Max(1f, fearCap);
            DefianceWindowHours = Math.Max(0.01f, defianceWindowHours);
            PoliceAttentionCap = Math.Max(1f, policeAttentionCap);
            PoliceAttentionHalfLifeHours = Math.Max(0.01f, policeAttentionHalfLifeHours);
            PoliceEscalation = Math.Max(0f, policeEscalation);
            PresenceFloor = Math.Min(1f, Math.Max(0f, presenceFloor));
            MemoryEntriesPerGang = Math.Max(4, memoryEntriesPerGang);
            MaxSeverity = Math.Max(1f, maxSeverity);

            // The design's fade bands: a week or two of quiet barely dents what a street
            // remembers of a killing, months erode it seriously. Hours, in game time.
            table = new TerritoryFearImpact[Categories.Count];
            Set(TerritoryFearCategory.Threat, new TerritoryFearImpact(6f, 72f, 0.05f));
            Set(TerritoryFearCategory.Assault, new TerritoryFearImpact(18f, 240f, 0.5f));
            Set(TerritoryFearCategory.PropertyDamage, new TerritoryFearImpact(10f, 168f, 0.4f));
            Set(TerritoryFearCategory.Shot, new TerritoryFearImpact(12f, 168f, 0.8f));
            Set(TerritoryFearCategory.Killing, new TerritoryFearImpact(40f, 504f, 1.2f));
            Set(TerritoryFearCategory.SuccessfulRetaliation,
                new TerritoryFearImpact(22f, 336f, 0.6f));
            Set(TerritoryFearCategory.IgnoredDefiance,
                new TerritoryFearImpact(-15f, 120f, 0f));

            if (overrides == null)
                return;
            foreach (var pair in overrides)
                Set(pair.Key, pair.Value);
        }

        /// <summary>Every act the model names, for a caller that must cover them all.
        /// Read-only: a shared static array would let one caller rewrite the list every
        /// other one iterates.</summary>
        public static IReadOnlyList<TerritoryFearCategory> Categories { get; } =
            (TerritoryFearCategory[])Enum.GetValues(typeof(TerritoryFearCategory));

        public static TerritoryFearConfig Default { get; } = new TerritoryFearConfig();

        public float HiddenWeight { get; }
        public float SeenWeight { get; }
        public float PublicWeight { get; }

        /// <summary>How much of an incident at one premise the whole block feels.</summary>
        public float PropagationFraction { get; }

        public float FearCap { get; }

        /// <summary>Game hours a house has to answer an open refusal before the street
        /// decides nothing was going to happen.</summary>
        public float DefianceWindowHours { get; }

        public float PoliceAttentionCap { get; }
        public float PoliceAttentionHalfLifeHours { get; }

        /// <summary>How much dearer violence gets while the police are already looking.</summary>
        public float PoliceEscalation { get; }

        /// <summary>How far police attention can hold a family's Presence down - the
        /// floor, not the multiplier: 0.25 means a red-hot block still counts a quarter
        /// of the men standing on it.</summary>
        public float PresenceFloor { get; }

        public int MemoryEntriesPerGang { get; }

        /// <summary>
        /// The most one act can be worth in multiples of itself. A street that has already
        /// watched a gunfight is not four times more frightened by four times the bullets,
        /// and without this ceiling one long exchange pins the block at the cap for weeks -
        /// which would make the loudest possible violence the only strategy worth having.
        /// </summary>
        public float MaxSeverity { get; }

        public TerritoryFearImpact Of(TerritoryFearCategory category) => table[(int)category];

        public float VisibilityWeight(TerritoryFearVisibility visibility)
        {
            switch (visibility)
            {
                case TerritoryFearVisibility.Hidden: return HiddenWeight;
                case TerritoryFearVisibility.Seen: return SeenWeight;
                default: return PublicWeight;
            }
        }

        /// <summary>What one act is worth here and now: the category's own weight, scaled
        /// by how much of it there was and by how many people watched.</summary>
        public float ImpactOf(TerritoryFearEvent value) =>
            Of(value.Category).Impact *
            Math.Min(MaxSeverity, Math.Max(0f, value.Severity)) *
            VisibilityWeight(value.Visibility);

        void Set(TerritoryFearCategory category, TerritoryFearImpact impact) =>
            table[(int)category] = impact;
    }

    /// <summary>One remembered act, with what it was worth and when it happened.</summary>
    public readonly struct TerritoryFearMemoryEntry
    {
        public TerritoryFearMemoryEntry(
            TerritoryFearCategory category,
            TerritoryFearVisibility visibility,
            float amount,
            float halfLifeHours,
            double gameHour,
            TerritoryBusinessId businessId)
        {
            Category = category;
            Visibility = visibility;
            Amount = amount;
            HalfLifeHours = halfLifeHours;
            GameHour = gameHour;
            BusinessId = businessId;
        }

        public TerritoryFearCategory Category { get; }
        public TerritoryFearVisibility Visibility { get; }

        /// <summary>What it was worth the moment it happened.</summary>
        public float Amount { get; }

        public float HalfLifeHours { get; }
        public double GameHour { get; }
        public TerritoryBusinessId BusinessId { get; }

        /// <summary>What is left of it now.</summary>
        public float At(double gameHour)
        {
            var elapsed = gameHour - GameHour;
            if (elapsed <= 0.0)
                return Amount;
            return Amount * (float)Math.Pow(0.5, elapsed / HalfLifeHours);
        }
    }

    /// <summary>A Gang×Block fear total that moved; the runtime turns it into an event.</summary>
    public readonly struct TerritoryFearChange
    {
        public TerritoryFearChange(
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

    /// <summary>An open refusal waiting to be answered (FEAR-010).</summary>
    public readonly struct TerritoryDefianceWatch
    {
        public TerritoryDefianceWatch(
            TerritoryGangId gangId,
            TerritoryBlockId blockId,
            TerritoryBusinessId businessId,
            double openedAt)
        {
            GangId = gangId;
            BlockId = blockId;
            BusinessId = businessId;
            OpenedAt = openedAt;
        }

        public TerritoryGangId GangId { get; }
        public TerritoryBlockId BlockId { get; }
        public TerritoryBusinessId BusinessId { get; }
        public double OpenedAt { get; }
    }

    /// <summary>
    /// What each street is afraid of, house by house.
    ///
    /// Fear is per family per block: one block can be terrified of the Falcones and
    /// unbothered by us, and the same family can be feared on one street and a rumour on
    /// the next. It is built out of remembered acts, not a running total - the current
    /// value is always the sum of what is left of each act, so decay, memory and the
    /// audit are the same arithmetic and cannot drift apart.
    ///
    /// Pure and time-free: the runtime records acts as they happen and evaluates on the
    /// Fear tick, in game hours off the territory scheduler, so it is frame-rate
    /// independent and pauses with the clock. It reads no GameObject, holds no control,
    /// and treats every family alike.
    /// </summary>
    public sealed class TerritoryFearLedger
    {
        const float Epsilon = 0.01f;

        readonly Dictionary<TerritoryBlockId, BlockRow> blocks =
            new Dictionary<TerritoryBlockId, BlockRow>();
        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();
        readonly List<TerritoryDefianceWatch> defiance = new List<TerritoryDefianceWatch>();
        readonly List<TerritoryBlockId> pruned = new List<TerritoryBlockId>();

        public TerritoryFearLedger(TerritoryFearConfig config = null) =>
            Config = config ?? TerritoryFearConfig.Default;

        public TerritoryFearConfig Config { get; set; }

        /// <summary>Every block that fears anybody, or that the police are watching.</summary>
        public IReadOnlyList<TerritoryBlockId> Blocks => blockIds;

        /// <summary>
        /// File one act. The full weight lands where it happened - on the premise if it
        /// had one - and a configured fraction of it is what the rest of the street feels
        /// (FEAR-007). An act nobody can pin on a house frightens the block and brings the
        /// police, but makes no family feared.
        /// </summary>
        public float Record(TerritoryFearEvent value)
        {
            if (!value.BlockId.IsValid)
                return 0f;

            var impact = Config.ImpactOf(value);
            var row = Block(value.BlockId);
            row.Police.Add(
                Math.Abs(impact) * Config.Of(value.Category).PoliceWeight, value.GameHour, Config);

            if (!value.IsAttributed || Math.Abs(impact) < 0.0001f)
                return impact;

            var halfLife = Config.Of(value.Category).MemoryHalfLifeHours;
            var blockShare = value.BusinessId.IsValid ? impact * Config.PropagationFraction : impact;

            row.Gang(value.GangId).Remember(new TerritoryFearMemoryEntry(
                value.Category, value.Visibility, blockShare, halfLife,
                value.GameHour, value.BusinessId), Config);

            if (value.BusinessId.IsValid)
                row.Business(value.BusinessId, value.GangId).Remember(
                    new TerritoryFearMemoryEntry(
                        value.Category, value.Visibility, impact, halfLife,
                        value.GameHour, value.BusinessId),
                    Config);

            return impact;
        }

        /// <summary>
        /// Fade what every street remembers and report what moved. Presence is not
        /// consulted and cannot reset a thing: a house whose men have all gone home is
        /// still the house that did this here.
        /// </summary>
        public void Evaluate(double gameHour, List<TerritoryFearChange> changes = null)
        {
            pruned.Clear();
            for (var i = 0; i < blockIds.Count; i++)
            {
                var blockId = blockIds[i];
                var row = blocks[blockId];
                row.Evaluate(blockId, gameHour, Config, changes);
                if (row.IsEmpty)
                    pruned.Add(blockId);
            }

            for (var i = 0; i < pruned.Count; i++)
            {
                blocks.Remove(pruned[i]);
                blockIds.Remove(pruned[i]);
            }
            pruned.Clear();
        }

        public float FearOf(TerritoryBlockId blockId, TerritoryGangId gangId, double gameHour) =>
            blocks.TryGetValue(blockId, out var row) ? row.FearOf(gangId, gameHour, Config) : 0f;

        /// <summary>What the street feels at all: the strongest fear of any one house.</summary>
        public float BlockFear(TerritoryBlockId blockId, double gameHour) =>
            blocks.TryGetValue(blockId, out var row) ? row.Strongest(gameHour, Config) : 0f;

        public float BusinessFear(
            TerritoryBlockId blockId,
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            double gameHour) =>
            blocks.TryGetValue(blockId, out var row)
                ? row.BusinessFear(businessId, gangId, gameHour, Config)
                : 0f;

        public float PoliceAttention(TerritoryBlockId blockId, double gameHour) =>
            blocks.TryGetValue(blockId, out var row) ? row.Police.At(gameHour, Config) : 0f;

        /// <summary>
        /// What a family's men are worth on this block while the police are looking. Never
        /// zero - a watched street is harder to hold, not impossible to stand on.
        /// </summary>
        public float PresenceScale(TerritoryBlockId blockId, double gameHour)
        {
            var attention = PoliceAttention(blockId, gameHour);
            if (attention <= 0f)
                return 1f;
            var share = Math.Min(1f, attention / Config.PoliceAttentionCap);
            return 1f - share * (1f - Config.PresenceFloor);
        }

        /// <summary>Every family this block fears, ascending by gang id.</summary>
        public void CollectGangs(
            TerritoryBlockId blockId, double gameHour, List<TerritoryGangValue> into)
        {
            into?.Clear();
            if (into != null && blocks.TryGetValue(blockId, out var row))
                row.CollectGangs(gameHour, Config, into);
        }

        /// <summary>What this street remembers of one house, newest first - the timeline
        /// the debug page explains the current number from.</summary>
        public void CollectMemory(
            TerritoryBlockId blockId,
            TerritoryGangId gangId,
            List<TerritoryFearMemoryEntry> into)
        {
            into?.Clear();
            if (into != null && blocks.TryGetValue(blockId, out var row))
                row.CollectMemory(gangId, into);
        }

        // ------------------------------------------------------------ open defiance

        /// <summary>
        /// A premise has openly refused a house. If nothing comes of it inside the
        /// window, the street draws its own conclusion (FEAR-010).
        /// </summary>
        public void OpenDefiance(
            TerritoryGangId gangId,
            TerritoryBlockId blockId,
            TerritoryBusinessId businessId,
            double gameHour)
        {
            if (!gangId.IsValid || !blockId.IsValid)
                return;
            for (var i = 0; i < defiance.Count; i++)
                if (defiance[i].GangId == gangId && defiance[i].BusinessId == businessId)
                    return;
            defiance.Add(new TerritoryDefianceWatch(gangId, blockId, businessId, gameHour));
        }

        /// <summary>The house answered: whatever it did, the refusal is no longer open.</summary>
        public bool AnswerDefiance(TerritoryGangId gangId, TerritoryBusinessId businessId)
        {
            for (var i = 0; i < defiance.Count; i++)
            {
                if (defiance[i].GangId != gangId || defiance[i].BusinessId != businessId)
                    continue;
                defiance.RemoveAt(i);
                return true;
            }
            return false;
        }

        public IReadOnlyList<TerritoryDefianceWatch> OpenDefiances => defiance;

        /// <summary>
        /// Close out every refusal nobody answered in time and file it as what it is: a
        /// house that let itself be told no. It costs reputation, never control - control
        /// hears about it later, through Fear.
        /// </summary>
        public void SweepDefiance(double gameHour, List<TerritoryFearEvent> emitted = null)
        {
            for (var i = defiance.Count - 1; i >= 0; i--)
            {
                var watch = defiance[i];
                if (gameHour - watch.OpenedAt < Config.DefianceWindowHours)
                    continue;

                defiance.RemoveAt(i);
                var value = new TerritoryFearEvent(
                    watch.GangId,
                    watch.BlockId,
                    TerritoryFearCategory.IgnoredDefiance,
                    1f,
                    TerritoryFearVisibility.Public,
                    gameHour,
                    watch.BusinessId);
                Record(value);
                emitted?.Add(value);
            }
        }

        BlockRow Block(TerritoryBlockId blockId)
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
            readonly List<GangFear> gangs = new List<GangFear>();
            readonly List<BusinessRow> businesses = new List<BusinessRow>();

            public PoliceAttentionValue Police { get; } = new PoliceAttentionValue();

            public bool IsEmpty => gangs.Count == 0 && businesses.Count == 0 && Police.IsQuiet;

            public GangFear Gang(TerritoryGangId gangId)
            {
                // Ascending by gang id, so the published order never depends on who
                // frightened the street first.
                for (var i = 0; i < gangs.Count; i++)
                {
                    if (gangs[i].GangId == gangId)
                        return gangs[i];
                    if (gangs[i].GangId.Value > gangId.Value)
                    {
                        var inserted = new GangFear(gangId);
                        gangs.Insert(i, inserted);
                        return inserted;
                    }
                }

                var appended = new GangFear(gangId);
                gangs.Add(appended);
                return appended;
            }

            public GangFear Business(TerritoryBusinessId businessId, TerritoryGangId gangId)
            {
                for (var i = 0; i < businesses.Count; i++)
                    if (businesses[i].BusinessId == businessId &&
                        businesses[i].Fear.GangId == gangId)
                        return businesses[i].Fear;

                var added = new BusinessRow(businessId, new GangFear(gangId));
                businesses.Add(added);
                return added.Fear;
            }

            public float FearOf(
                TerritoryGangId gangId, double gameHour, TerritoryFearConfig config)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        return gangs[i].Value(gameHour, config);
                return 0f;
            }

            public float Strongest(double gameHour, TerritoryFearConfig config)
            {
                var best = 0f;
                for (var i = 0; i < gangs.Count; i++)
                {
                    var value = gangs[i].Value(gameHour, config);
                    if (value > best)
                        best = value;
                }
                return best;
            }

            public float BusinessFear(
                TerritoryBusinessId businessId,
                TerritoryGangId gangId,
                double gameHour,
                TerritoryFearConfig config)
            {
                // What a shopkeeper feels is what the street feels plus what was done to
                // him: the block's share is already in the block total, so the premise's
                // own memory is added on top of it.
                var own = 0f;
                for (var i = 0; i < businesses.Count; i++)
                    if (businesses[i].BusinessId == businessId &&
                        businesses[i].Fear.GangId == gangId)
                        own = businesses[i].Fear.Value(gameHour, config);
                return Math.Min(config.FearCap, own + FearOf(gangId, gameHour, config));
            }

            public void CollectGangs(
                double gameHour, TerritoryFearConfig config, List<TerritoryGangValue> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    var value = gangs[i].Value(gameHour, config);
                    if (value <= 0f)
                        continue;
                    into.Add(new TerritoryGangValue(gangs[i].GangId, value));
                }
            }

            public void CollectMemory(
                TerritoryGangId gangId, List<TerritoryFearMemoryEntry> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        gangs[i].CollectMemory(into);
            }

            public void Evaluate(
                TerritoryBlockId blockId,
                double gameHour,
                TerritoryFearConfig config,
                List<TerritoryFearChange> changes)
            {
                for (var i = gangs.Count - 1; i >= 0; i--)
                {
                    var gang = gangs[i];
                    var previous = gang.Published;
                    var current = gang.Value(gameHour, config);
                    gang.Forget(gameHour, config);
                    if (changes != null && Math.Abs(current - previous) >= Epsilon)
                        changes.Add(new TerritoryFearChange(
                            blockId, gang.GangId, previous, current));
                    gang.Published = current;
                    if (gang.IsEmpty)
                        gangs.RemoveAt(i);
                }

                for (var i = businesses.Count - 1; i >= 0; i--)
                {
                    businesses[i].Fear.Forget(gameHour, config);
                    if (businesses[i].Fear.IsEmpty)
                        businesses.RemoveAt(i);
                }

                Police.Forget(gameHour, config);
            }
        }

        sealed class BusinessRow
        {
            public BusinessRow(TerritoryBusinessId businessId, GangFear fear)
            {
                BusinessId = businessId;
                Fear = fear;
            }

            public TerritoryBusinessId BusinessId { get; }
            public GangFear Fear { get; }
        }

        sealed class GangFear
        {
            readonly List<TerritoryFearMemoryEntry> memory =
                new List<TerritoryFearMemoryEntry>();

            public GangFear(TerritoryGangId gangId) => GangId = gangId;

            public TerritoryGangId GangId { get; }

            /// <summary>What the block last published, so a change can be announced.</summary>
            public float Published { get; set; }

            public bool IsEmpty => memory.Count == 0;

            /// <summary>
            /// Remember one act. The list is bounded: past the cap the two oldest are
            /// folded into one entry that keeps their combined weight and the longer of
            /// their two memories, so a night-long gunfight cannot grow without end and
            /// the total it leaves behind stays what it was.
            /// </summary>
            public void Remember(TerritoryFearMemoryEntry entry, TerritoryFearConfig config)
            {
                memory.Add(entry);
                while (memory.Count > config.MemoryEntriesPerGang)
                {
                    var first = memory[0];
                    var second = memory[1];
                    // Fold onto the newer of the two timestamps, carrying the older
                    // entry's remaining weight as of that moment.
                    var folded = new TerritoryFearMemoryEntry(
                        first.Category,
                        first.Visibility,
                        first.At(second.GameHour) + second.Amount,
                        Math.Max(first.HalfLifeHours, second.HalfLifeHours),
                        second.GameHour,
                        second.BusinessId);
                    memory.RemoveAt(0);
                    memory[0] = folded;
                }
            }

            public float Value(double gameHour, TerritoryFearConfig config)
            {
                var total = 0f;
                for (var i = 0; i < memory.Count; i++)
                    total += memory[i].At(gameHour);
                if (total < 0f)
                    total = 0f;
                return Math.Min(config.FearCap, total);
            }

            /// <summary>Drop what is left of nothing, so a long soak does not carry a
            /// thousand spent acts around forever.</summary>
            public void Forget(double gameHour, TerritoryFearConfig config)
            {
                for (var i = memory.Count - 1; i >= 0; i--)
                    if (Math.Abs(memory[i].At(gameHour)) < Epsilon)
                        memory.RemoveAt(i);
            }

            public void CollectMemory(List<TerritoryFearMemoryEntry> into)
            {
                for (var i = memory.Count - 1; i >= 0; i--)
                    into.Add(memory[i]);
            }
        }

        /// <summary>
        /// How hard the law is looking at this block. Violence buys Fear and it buys
        /// this, and the second bill grows the louder the street already is (FEAR-013).
        /// </summary>
        public sealed class PoliceAttentionValue
        {
            float amount;
            double stamp;

            public bool IsQuiet => amount < Epsilon;

            public float At(double gameHour, TerritoryFearConfig config)
            {
                if (amount <= 0f)
                    return 0f;
                var elapsed = gameHour - stamp;
                if (elapsed <= 0.0)
                    return amount;
                return amount *
                       (float)Math.Pow(0.5, elapsed / config.PoliceAttentionHalfLifeHours);
            }

            public void Add(float weight, double gameHour, TerritoryFearConfig config)
            {
                if (weight <= 0f)
                    return;
                var current = At(gameHour, config);
                var heat = current / config.PoliceAttentionCap;
                amount = Math.Min(
                    config.PoliceAttentionCap,
                    current + weight * (1f + heat * config.PoliceEscalation));
                stamp = gameHour;
            }

            public void Forget(double gameHour, TerritoryFearConfig config)
            {
                amount = At(gameHour, config);
                stamp = gameHour;
                if (amount < Epsilon)
                    amount = 0f;
            }
        }
    }
}
