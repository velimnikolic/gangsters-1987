using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// THE block model. Every Phase-1 system - Presence, Fear, business compliance,
    /// derived control, the ledger, the maps - names a block with this record and this
    /// record only; <see cref="RoadDemo.CoreBlockDefinition"/> is the plan it is read
    /// from, and anything else that carries block rectangles (Gameplay.CityBlocks, the
    /// map HUDs' own tables) is presentation or legacy and must be fed from here.
    ///
    /// It is deliberately immutable geography and identity: no OwnerGangId, no
    /// ControlledBy, no capture progress. Who holds a block is derived from signals in
    /// <see cref="TerritorySimulationState"/>, never stamped on the geography.
    /// </summary>
    public sealed class TerritoryBlockDefinition
    {
        public TerritoryBlockDefinition(
            TerritoryBlockId id,
            int legacyBlockId,
            TerritoryNeighborhoodId neighborhoodId,
            string neighborhoodName,
            string displayName,
            TerritoryBounds worldBounds,
            string identitySource,
            string sourceKind = "")
        {
            if (!id.IsValid)
                throw new ArgumentException("A territory block requires a canonical ID.", nameof(id));

            Id = id;
            LegacyBlockId = legacyBlockId;
            NeighborhoodId = neighborhoodId;
            NeighborhoodName = neighborhoodName ?? "";
            DisplayName = displayName ?? id.Value;
            WorldBounds = worldBounds;
            IdentitySource = identitySource ?? "";
            SourceKind = sourceKind ?? "";
        }

        public TerritoryBlockId Id { get; }
        public int LegacyBlockId { get; }
        public TerritoryNeighborhoodId NeighborhoodId { get; }
        public string NeighborhoodName { get; }
        public string DisplayName { get; }
        public TerritoryBounds WorldBounds { get; }

        /// <summary>The middle of the block's world footprint - derived, never stored
        /// twice: a consumer that needs somewhere to march to, plot a label at or measure
        /// a distance from asks the block rather than re-deriving a rectangle.</summary>
        public TerritoryPoint Center => WorldBounds.Center;

        public string IdentitySource { get; }

        /// <summary>What the plan says the block IS - "res", "park", "yard-lot", "quay",
        /// "apron", "bank", or the source prefab's name. Presentation reads it (a map card
        /// wants a zone colour); no simulation rule may branch on it, because it is a
        /// description of the ground and not of who holds it.</summary>
        public string SourceKind { get; }
    }

    /// <summary>
    /// Command responsibility for a block. This is an assignment in the organization
    /// graph, never ownership or control of the block itself.
    /// </summary>
    public readonly struct TerritoryResponsibility : IEquatable<TerritoryResponsibility>
    {
        public TerritoryResponsibility(
            TerritoryGangId gangId,
            TerritoryCharacterId bossId,
            TerritoryCharacterId lieutenantId,
            TerritoryCommandNodeId commandNodeId)
        {
            GangId = gangId;
            BossId = bossId;
            LieutenantId = lieutenantId;
            CommandNodeId = commandNodeId;
        }

        public TerritoryGangId GangId { get; }
        public TerritoryCharacterId BossId { get; }
        public TerritoryCharacterId LieutenantId { get; }
        public TerritoryCommandNodeId CommandNodeId { get; }
        public bool IsAssigned => GangId.IsValid || BossId.IsValid ||
                                  LieutenantId.IsValid || CommandNodeId.IsValid;

        public bool Equals(TerritoryResponsibility other) =>
            GangId == other.GangId && BossId == other.BossId &&
            LieutenantId == other.LieutenantId && CommandNodeId == other.CommandNodeId;

        public override bool Equals(object obj) =>
            obj is TerritoryResponsibility other && Equals(other);

        public override int GetHashCode()
        {
            var hash = GangId.GetHashCode();
            hash = hash * 397 ^ BossId.GetHashCode();
            hash = hash * 397 ^ LieutenantId.GetHashCode();
            return hash * 397 ^ CommandNodeId.GetHashCode();
        }

        public static bool operator ==(TerritoryResponsibility left, TerritoryResponsibility right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryResponsibility left, TerritoryResponsibility right) =>
            !left.Equals(right);
    }

    /// <summary>Exact per-gang values as seen by the simulation/debug query.</summary>
    public readonly struct TerritoryGangSignals
    {
        public TerritoryGangSignals(
            TerritoryGangId gangId, float presence, float influence, float fear = 0f)
        {
            GangId = gangId;
            Presence = presence;
            Influence = influence;
            Fear = fear;
        }

        public TerritoryGangId GangId { get; }

        /// <summary>Bodies on the ground, weighted - EPIC 4 owns it.</summary>
        public float Presence { get; }

        /// <summary>Share of the block's premises held - the control pass owns it.</summary>
        public float Influence { get; }

        /// <summary>How much this street fears THIS family - EPIC 5 owns it. Fear is
        /// per family: a block can be terrified of one house and unbothered by another.</summary>
        public float Fear { get; }
    }

    /// <summary>
    /// Derived control is a query result, not a hard OwnerOfBlock fact. The foundation
    /// stores no capture progress, capture timer, or block owner.
    /// </summary>
    public enum TerritoryControlState
    {
        Unknown,

        /// <summary>The design plan's "Neutral": nobody's street.</summary>
        Uncontrolled,

        Influenced,
        Contested,
        Controlled,

        // Appended so existing serialized values keep their meaning.

        /// <summary>Held so completely that nobody else is worth naming on it.</summary>
        Dominated,
    }

    /// <summary>
    /// Immutable exact values supplied by simulation systems. This ticket defines their
    /// storage/query boundary only; it intentionally implements no Presence/Fear decay,
    /// compliance rule, or control formula.
    /// </summary>
    public sealed class TerritoryBlockSignals
    {
        static readonly TerritoryGangSignals[] NoGangSignals = Array.Empty<TerritoryGangSignals>();

        public static TerritoryBlockSignals Empty { get; } = new TerritoryBlockSignals();

        public TerritoryBlockSignals(
            float? localFear = null,
            float? businessCompliance = null,
            int compliantBusinesses = 0,
            int totalBusinesses = 0,
            TerritoryControlState control = TerritoryControlState.Unknown,
            TerritoryGangId leadingGangId = default,
            IReadOnlyList<TerritoryGangSignals> gangs = null)
        {
            LocalFear = localFear;
            BusinessCompliance = businessCompliance;
            CompliantBusinesses = Math.Max(0, compliantBusinesses);
            TotalBusinesses = Math.Max(0, totalBusinesses);
            Control = control;
            LeadingGangId = leadingGangId;

            if (gangs == null || gangs.Count == 0)
            {
                Gangs = NoGangSignals;
            }
            else
            {
                var copy = new TerritoryGangSignals[gangs.Count];
                for (var i = 0; i < gangs.Count; i++)
                    copy[i] = gangs[i];
                Gangs = copy;
            }
        }

        /// <summary>What the street feels at all, whoever caused it: the STRONGEST
        /// per-family fear on the block (see <see cref="TerritoryGangSignals.Fear"/>), so
        /// a page that only wants "is this street frightened" has one number to read.
        /// Who exactly they are afraid of is the per-gang value, never this one.</summary>
        public float? LocalFear { get; }
        public float? BusinessCompliance { get; }
        public int CompliantBusinesses { get; }
        public int TotalBusinesses { get; }
        public bool HasBusinessCount => TotalBusinesses > 0;
        public TerritoryControlState Control { get; }
        public TerritoryGangId LeadingGangId { get; }
        public IReadOnlyList<TerritoryGangSignals> Gangs { get; }

        public bool TryGetGang(TerritoryGangId gangId, out TerritoryGangSignals signals)
        {
            for (var i = 0; i < Gangs.Count; i++)
            {
                if (Gangs[i].GangId != gangId)
                    continue;
                signals = Gangs[i];
                return true;
            }

            signals = default;
            return false;
        }
    }

    /// <summary>One currently observable physical actor; no GameObject crosses the query boundary.</summary>
    public readonly struct TerritoryActorObservation
    {
        public TerritoryActorObservation(
            TerritoryCharacterId characterId,
            TerritoryGangId gangId,
            TerritoryCommandNodeId groupId,
            string displayName,
            string gangName,
            bool leadsGroup,
            TerritoryRank rank = TerritoryRank.Unknown,
            TerritoryActorActivity activity = TerritoryActorActivity.Unknown)
        {
            CharacterId = characterId;
            GangId = gangId;
            GroupId = groupId;
            DisplayName = displayName ?? "";
            GangName = gangName ?? "";
            LeadsGroup = leadsGroup;
            Rank = rank;
            Activity = activity;
        }

        public TerritoryCharacterId CharacterId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryCommandNodeId GroupId { get; }
        public string DisplayName { get; }
        public string GangName { get; }
        public bool LeadsGroup { get; }

        /// <summary>What this body IS, resolved from real personnel identity. Presence
        /// weights it; command responsibility for the block does not enter into it.</summary>
        public TerritoryRank Rank { get; }

        /// <summary>What this body is DOING here, read off the physical truth the project
        /// already keeps. A man riding through is not a man holding the ground.</summary>
        public TerritoryActorActivity Activity { get; }
    }

    public interface ITerritoryActorSource
    {
        void CollectActors(TerritoryBlockId blockId, List<TerritoryActorObservation> into);
    }

    public interface ITerritoryResponsibilityNameSource
    {
        string CharacterName(TerritoryCharacterId characterId);
    }

    public readonly struct TerritoryResponsibilityView
    {
        public TerritoryResponsibilityView(
            TerritoryResponsibility responsibility, string bossName, string lieutenantName)
        {
            Responsibility = responsibility;
            BossName = bossName ?? "";
            LieutenantName = lieutenantName ?? "";
        }

        public TerritoryResponsibility Responsibility { get; }
        public string BossName { get; }
        public string LieutenantName { get; }
    }

    /// <summary>Read-only, cheap-to-rebuild snapshot returned to debug/query consumers.</summary>
    public sealed class TerritoryBlockTruth
    {
        public TerritoryBlockTruth(
            TerritoryBlockDefinition definition,
            TerritoryResponsibilityView responsibility,
            IReadOnlyList<TerritoryActorObservation> actors,
            TerritoryBlockSignals signals)
        {
            Definition = definition;
            Responsibility = responsibility;
            Actors = actors ?? Array.Empty<TerritoryActorObservation>();
            Signals = signals ?? TerritoryBlockSignals.Empty;
        }

        public TerritoryBlockDefinition Definition { get; }
        public TerritoryResponsibilityView Responsibility { get; }
        public IReadOnlyList<TerritoryActorObservation> Actors { get; }
        public TerritoryBlockSignals Signals { get; }
    }

    public interface ITerritoryTruthQuery
    {
        IReadOnlyList<TerritoryBlockId> BlockIds { get; }
        bool TryGetBlock(TerritoryBlockId blockId, out TerritoryBlockTruth truth);
    }

    /// <summary>
    /// The authoritative owner for new Phase-1 territory simulation state. Definitions,
    /// responsibility and exact signals live here; events and presentation are projections.
    /// Public consumers receive only ITerritoryTruthQuery or ITerritoryPlayerQuery. Future
    /// Presence/Fear/compliance/control systems update this store from simulation code, never UI.
    /// </summary>
    public sealed class TerritorySimulationState
    {
        readonly Dictionary<TerritoryBlockId, TerritoryBlockDefinition> definitions =
            new Dictionary<TerritoryBlockId, TerritoryBlockDefinition>();
        readonly Dictionary<TerritoryBlockId, TerritoryResponsibility> responsibilities =
            new Dictionary<TerritoryBlockId, TerritoryResponsibility>();
        readonly Dictionary<TerritoryBlockId, TerritoryBlockSignals> signals =
            new Dictionary<TerritoryBlockId, TerritoryBlockSignals>();
        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();

        public TerritorySimulationState(IEnumerable<TerritoryBlockDefinition> blocks)
        {
            if (blocks == null)
                return;

            foreach (var block in blocks)
            {
                if (block == null || definitions.ContainsKey(block.Id))
                    continue;
                definitions.Add(block.Id, block);
                blockIds.Add(block.Id);
            }
        }

        public int Version { get; private set; }
        internal IReadOnlyList<TerritoryBlockId> BlockIds => blockIds;

        internal bool TryGetDefinition(TerritoryBlockId blockId, out TerritoryBlockDefinition block) =>
            definitions.TryGetValue(blockId, out block);

        internal TerritoryResponsibility ResponsibilityOf(TerritoryBlockId blockId) =>
            responsibilities.TryGetValue(blockId, out var value) ? value : default;

        internal TerritoryBlockSignals SignalsOf(TerritoryBlockId blockId) =>
            signals.TryGetValue(blockId, out var value) ? value : TerritoryBlockSignals.Empty;

        internal bool AssignResponsibility(
            TerritoryBlockId blockId, TerritoryResponsibility responsibility)
        {
            if (!definitions.ContainsKey(blockId))
                return false;

            if (responsibilities.TryGetValue(blockId, out var current) && current == responsibility)
                return true;

            responsibilities[blockId] = responsibility;
            Version++;
            return true;
        }

        internal bool ClearResponsibility(TerritoryBlockId blockId)
        {
            if (!definitions.ContainsKey(blockId))
                return false;
            if (!responsibilities.Remove(blockId))
                return true;
            Version++;
            return true;
        }

        /// <summary>
        /// Simulation-writer seam for later Phase-1 tickets. There is deliberately no
        /// public setter: a view can rebuild from this value but cannot establish it.
        /// </summary>
        internal bool SetSignals(TerritoryBlockId blockId, TerritoryBlockSignals exactSignals)
        {
            if (!definitions.ContainsKey(blockId) || exactSignals == null)
                return false;
            signals[blockId] = exactSignals;
            Version++;
            return true;
        }
    }

    /// <summary>Full simulation/debug truth, composed on demand from authoritative sources.</summary>
    public sealed class TerritoryTruthQuery : ITerritoryTruthQuery
    {
        readonly TerritorySimulationState state;
        readonly ITerritoryActorSource actors;
        readonly ITerritoryResponsibilityNameSource names;

        public TerritoryTruthQuery(
            TerritorySimulationState state,
            ITerritoryActorSource actors = null,
            ITerritoryResponsibilityNameSource names = null)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.actors = actors;
            this.names = names;
        }

        public IReadOnlyList<TerritoryBlockId> BlockIds => state.BlockIds;

        public bool TryGetBlock(TerritoryBlockId blockId, out TerritoryBlockTruth truth)
        {
            if (!state.TryGetDefinition(blockId, out var definition))
            {
                truth = null;
                return false;
            }

            var responsibility = state.ResponsibilityOf(blockId);
            var bossName = responsibility.BossId.IsValid && names != null
                ? names.CharacterName(responsibility.BossId)
                : "";
            var lieutenantName = responsibility.LieutenantId.IsValid && names != null
                ? names.CharacterName(responsibility.LieutenantId)
                : "";

            IReadOnlyList<TerritoryActorObservation> observed =
                Array.Empty<TerritoryActorObservation>();
            if (actors != null)
            {
                var collected = new List<TerritoryActorObservation>();
                actors.CollectActors(blockId, collected);
                observed = collected;
            }

            truth = new TerritoryBlockTruth(
                definition,
                new TerritoryResponsibilityView(responsibility, bossName, lieutenantName),
                observed,
                state.SignalsOf(blockId));
            return true;
        }
    }
}
