using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>Configurable exact-to-qualitative mapping; it owns labels, never gameplay.</summary>
    public sealed class TerritoryQualitativeScale
    {
        public TerritoryQualitativeScale(
            float weakAt,
            float moderateAt,
            float strongAt,
            float dominantAt = float.PositiveInfinity,
            string noneLabel = "None",
            string weakLabel = "Weak",
            string moderateLabel = "Moderate",
            string strongLabel = "Strong",
            string dominantLabel = "Dominant",
            string unknownLabel = "Unknown")
        {
            if (weakAt > moderateAt || moderateAt > strongAt || strongAt > dominantAt)
                throw new ArgumentException("Territory presentation thresholds must be ascending.");

            WeakAt = weakAt;
            ModerateAt = moderateAt;
            StrongAt = strongAt;
            DominantAt = dominantAt;
            NoneLabel = noneLabel ?? "None";
            WeakLabel = weakLabel ?? "Weak";
            ModerateLabel = moderateLabel ?? "Moderate";
            StrongLabel = strongLabel ?? "Strong";
            DominantLabel = dominantLabel ?? "Dominant";
            UnknownLabel = unknownLabel ?? "Unknown";
        }

        public float WeakAt { get; }
        public float ModerateAt { get; }
        public float StrongAt { get; }

        /// <summary>The top band. Left at infinity a scale simply never reaches it, so a
        /// signal that has no "dominant" to speak of keeps the four words it had.</summary>
        public float DominantAt { get; }

        public string NoneLabel { get; }
        public string WeakLabel { get; }
        public string ModerateLabel { get; }
        public string StrongLabel { get; }
        public string DominantLabel { get; }
        public string UnknownLabel { get; }

        public string Describe(float? exact)
        {
            if (!exact.HasValue)
                return UnknownLabel;
            if (exact.Value < WeakAt)
                return NoneLabel;
            if (exact.Value < ModerateAt)
                return WeakLabel;
            if (exact.Value < StrongAt)
                return ModerateLabel;
            if (exact.Value < DominantAt)
                return StrongLabel;
            return DominantLabel;
        }
    }

    /// <summary>
    /// Player-facing vocabulary and thresholds. Replacing this profile can repaint the
    /// same truth differently without writing a single simulation value.
    /// </summary>
    public sealed class TerritoryPresentationProfile
    {
        public TerritoryPresentationProfile(
            TerritoryQualitativeScale presence,
            TerritoryQualitativeScale fear,
            TerritoryQualitativeScale compliance,
            TerritoryQualitativeScale rivalActivity,
            TerritoryQualitativeScale holding = null,
            string uncontrolled = "Uncontrolled",
            string influenced = "Influenced",
            string contested = "Contested",
            string controlled = "Controlled",
            string unknownControl = "Unknown",
            string dominated = "Dominated")
        {
            Presence = presence ?? throw new ArgumentNullException(nameof(presence));
            Fear = fear ?? throw new ArgumentNullException(nameof(fear));
            Compliance = compliance ?? throw new ArgumentNullException(nameof(compliance));
            RivalActivity = rivalActivity ?? throw new ArgumentNullException(nameof(rivalActivity));
            // Deeds held, in the same words the deed share was always read in.
            Holding = holding ?? new TerritoryQualitativeScale(0.01f, 25f, 60f);
            Uncontrolled = uncontrolled ?? "Uncontrolled";
            Influenced = influenced ?? "Influenced";
            Contested = contested ?? "Contested";
            Controlled = controlled ?? "Controlled";
            UnknownControl = unknownControl ?? "Unknown";
            Dominated = dominated ?? "Dominated";
        }

        public TerritoryQualitativeScale Presence { get; }
        public TerritoryQualitativeScale Fear { get; }
        public TerritoryQualitativeScale Compliance { get; }
        public TerritoryQualitativeScale RivalActivity { get; }

        /// <summary>The words for a family's share of the premises on a block.</summary>
        public TerritoryQualitativeScale Holding { get; }

        public string Uncontrolled { get; }
        public string Influenced { get; }
        public string Contested { get; }
        public string Controlled { get; }
        public string UnknownControl { get; }

        /// <summary>A street held so heavily nobody else is worth naming on it.</summary>
        public string Dominated { get; }

        public static TerritoryPresentationProfile Default { get; } =
            new TerritoryPresentationProfile(
                // Presence carries the fifth word: a street can be held so heavily that
                // "Strong" stops saying anything (PRES-009).
                new TerritoryQualitativeScale(0.01f, 25f, 60f, 85f),
                // What a street feels, in the words a street uses.
                new TerritoryQualitativeScale(
                    0.01f, 25f, 60f, float.PositiveInfinity,
                    "Calm", "Uneasy", "Afraid", "Terrified"),
                new TerritoryQualitativeScale(0.01f, 35f, 70f),
                new TerritoryQualitativeScale(0.01f, 25f, 60f));
    }

    /// <summary>
    /// Ephemeral knowledge projection. A future intelligence system can hide or soften
    /// rival signals here without copying or changing authoritative state.
    /// </summary>
    public interface ITerritoryKnowledgeFilter
    {
        TerritoryBlockSignals Observe(
            TerritoryBlockTruth truth, TerritoryGangId viewingGangId);
    }

    /// <summary>Phase-1 policy: expose the available signals while preserving the seam.</summary>
    public sealed class FullTerritoryKnowledgeFilter : ITerritoryKnowledgeFilter
    {
        public static FullTerritoryKnowledgeFilter Instance { get; } =
            new FullTerritoryKnowledgeFilter();

        FullTerritoryKnowledgeFilter() { }

        public TerritoryBlockSignals Observe(
            TerritoryBlockTruth truth, TerritoryGangId viewingGangId) =>
            truth?.Signals ?? TerritoryBlockSignals.Empty;
    }

    /// <summary>
    /// How an owner on this block is likely to take a visit from the viewing family -
    /// a hint for interaction tone and status lines, not a dialogue system. It is read
    /// off Fear and nothing else, and it decides nothing.
    /// </summary>
    public enum TerritoryOwnerTone
    {
        Unknown,
        Easy,
        Wary,
        Fearful,
        Cowed,
    }

    /// <summary>Immutable qualitative model consumed by normal territory UI.</summary>
    public sealed class TerritoryBlockPresentation
    {
        public TerritoryBlockPresentation(
            TerritoryBlockId blockId,
            string blockName,
            TerritoryNeighborhoodId neighborhoodId,
            string neighborhoodName,
            string presence,
            string localFear,
            string businesses,
            string rivalActivity,
            string control,
            string holding = "Unknown",
            string rivalPresence = "Unknown",
            string fearOfUs = "Unknown",
            TerritoryOwnerTone ownerTone = TerritoryOwnerTone.Unknown)
        {
            BlockId = blockId;
            BlockName = blockName ?? "";
            NeighborhoodId = neighborhoodId;
            NeighborhoodName = neighborhoodName ?? "";
            Presence = presence ?? "Unknown";
            LocalFear = localFear ?? "Unknown";
            Businesses = businesses ?? "Unknown";
            RivalActivity = rivalActivity ?? "Unknown";
            Control = control ?? "Unknown";
            Holding = holding ?? "Unknown";
            RivalPresence = rivalPresence ?? "Unknown";
            FearOfUs = fearOfUs ?? "Unknown";
            OwnerTone = ownerTone;
        }

        public TerritoryBlockId BlockId { get; }
        public string BlockName { get; }
        public TerritoryNeighborhoodId NeighborhoodId { get; }
        public string NeighborhoodName { get; }
        /// <summary>How heavily the viewing family's own men stand on this block.</summary>
        public string Presence { get; }

        public string LocalFear { get; }
        public string Businesses { get; }

        /// <summary>The strongest rival's hold on the block's premises, as far as the
        /// viewer is allowed to know it.</summary>
        public string RivalActivity { get; }

        public string Control { get; }

        /// <summary>The viewing family's own share of the block's premises. Standing on a
        /// street and holding deeds on it are two different claims and the ledger says
        /// which is which.</summary>
        public string Holding { get; }

        /// <summary>The strongest rival's physical Presence, filtered by what the viewer
        /// knows - never the rival's exact number.</summary>
        public string RivalPresence { get; }

        /// <summary>How much this street fears the VIEWING family. LocalFear says the
        /// street is frightened; this says whether it is frightened of us.</summary>
        public string FearOfUs { get; }

        /// <summary>How an owner here is likely to take our visit.</summary>
        public TerritoryOwnerTone OwnerTone { get; }
    }

    /// <summary>
    /// What the player may read about one business. Words only: no exact fear, no
    /// presence number, no capture percentage, and no setter - the only way anything here
    /// changes is a valid intent going the other way through the command gateway.
    /// </summary>
    public sealed class TerritoryBusinessPresentation
    {
        public TerritoryBusinessPresentation(
            TerritoryBusinessId businessId,
            string businessName,
            string blockName,
            string standing,
            string protector,
            string localSituation,
            TerritoryOwnerTone ownerTone,
            bool hasRecentTrouble)
        {
            BusinessId = businessId;
            BusinessName = businessName ?? "";
            BlockName = blockName ?? "";
            Standing = standing ?? "Unknown";
            Protector = protector ?? "";
            LocalSituation = localSituation ?? "Unknown";
            OwnerTone = ownerTone;
            HasRecentTrouble = hasRecentTrouble;
        }

        public TerritoryBusinessId BusinessId { get; }
        public string BusinessName { get; }
        public string BlockName { get; }

        /// <summary>Where the shop stands with the VIEWING family, in words.</summary>
        public string Standing { get; }

        /// <summary>The family it pays, when the viewer can be expected to know - empty
        /// when it pays nobody, or when the knowledge filter has not shown it.</summary>
        public string Protector { get; }

        /// <summary>What the street around it feels.</summary>
        public string LocalSituation { get; }

        public TerritoryOwnerTone OwnerTone { get; }

        /// <summary>Something happened here lately. Not how much, not to whom.</summary>
        public bool HasRecentTrouble { get; }
    }

    /// <summary>The words a shop's standing is read in. Config, never a rule.</summary>
    public sealed class TerritoryStandingVocabulary
    {
        public static TerritoryStandingVocabulary Default { get; } =
            new TerritoryStandingVocabulary();

        public string Describe(TerritoryProtectionState state)
        {
            switch (state)
            {
                case TerritoryProtectionState.Approached: return "Approached";
                case TerritoryProtectionState.Hesitant: return "Wavering";
                case TerritoryProtectionState.Intimidated: return "Leaned on";
                case TerritoryProtectionState.Compliant: return "Paying us";
                case TerritoryProtectionState.Defiant: return "Refused us";
                default: return "Nothing yet";
            }
        }
    }

    public interface ITerritoryPlayerQuery
    {
        IReadOnlyList<TerritoryBlockId> BlockIds { get; }
        bool TryGetBlock(TerritoryBlockId blockId, out TerritoryBlockPresentation presentation);
    }

    /// <summary>Pure, allocation-light projection from exact truth to player vocabulary.</summary>
    public sealed class TerritoryPresentationProjector
    {
        readonly TerritoryPresentationProfile profile;

        public TerritoryPresentationProjector(TerritoryPresentationProfile profile) =>
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

        public TerritoryBlockPresentation Project(
            TerritoryBlockTruth truth,
            TerritoryBlockSignals observed,
            TerritoryGangId viewingGangId)
        {
            if (truth == null)
                throw new ArgumentNullException(nameof(truth));

            observed ??= TerritoryBlockSignals.Empty;

            float? ownPresence = null;
            float? ownHolding = null;
            float? ownFear = null;
            float? strongestRival = null;
            float? strongestRivalPresence = null;
            for (var i = 0; i < observed.Gangs.Count; i++)
            {
                var gang = observed.Gangs[i];
                if (gang.GangId == viewingGangId)
                {
                    ownPresence = gang.Presence;
                    ownHolding = gang.Influence;
                    ownFear = gang.Fear;
                    continue;
                }

                if (!strongestRival.HasValue || gang.Influence > strongestRival.Value)
                    strongestRival = gang.Influence;
                if (!strongestRivalPresence.HasValue ||
                    gang.Presence > strongestRivalPresence.Value)
                    strongestRivalPresence = gang.Presence;
            }

            var businesses = observed.HasBusinessCount
                ? $"{observed.CompliantBusinesses}/{observed.TotalBusinesses} compliant"
                : profile.Compliance.Describe(observed.BusinessCompliance);

            return new TerritoryBlockPresentation(
                truth.Definition.Id,
                truth.Definition.DisplayName,
                truth.Definition.NeighborhoodId,
                truth.Definition.NeighborhoodName,
                profile.Presence.Describe(ownPresence),
                profile.Fear.Describe(observed.LocalFear),
                businesses,
                profile.RivalActivity.Describe(strongestRival),
                ControlLabel(observed.Control),
                profile.Holding.Describe(ownHolding),
                profile.Presence.Describe(strongestRivalPresence),
                profile.Fear.Describe(ownFear),
                Tone(ownFear));
        }

        /// <summary>The same thresholds the words are read off, as a hint an interaction
        /// can branch on without parsing a label string.</summary>
        TerritoryOwnerTone Tone(float? fear)
        {
            if (!fear.HasValue)
                return TerritoryOwnerTone.Unknown;
            if (fear.Value < profile.Fear.WeakAt)
                return TerritoryOwnerTone.Easy;
            if (fear.Value < profile.Fear.ModerateAt)
                return TerritoryOwnerTone.Wary;
            if (fear.Value < profile.Fear.StrongAt)
                return TerritoryOwnerTone.Fearful;
            return TerritoryOwnerTone.Cowed;
        }

        string ControlLabel(TerritoryControlState control)
        {
            switch (control)
            {
                case TerritoryControlState.Uncontrolled: return profile.Uncontrolled;
                case TerritoryControlState.Influenced: return profile.Influenced;
                case TerritoryControlState.Contested: return profile.Contested;
                case TerritoryControlState.Controlled: return profile.Controlled;
                case TerritoryControlState.Dominated: return profile.Dominated;
                default: return profile.UnknownControl;
            }
        }
    }

    /// <summary>
    /// The normal-player read path. It cannot return exact rival numbers: truth passes
    /// through a knowledge filter and then becomes an immutable qualitative projection.
    /// </summary>
    public sealed class TerritoryPlayerQuery : ITerritoryPlayerQuery
    {
        readonly ITerritoryTruthQuery truth;
        readonly TerritoryGangId viewingGangId;
        readonly ITerritoryKnowledgeFilter knowledge;
        readonly TerritoryPresentationProjector projector;

        public TerritoryPlayerQuery(
            ITerritoryTruthQuery truth,
            TerritoryGangId viewingGangId,
            TerritoryPresentationProfile profile,
            ITerritoryKnowledgeFilter knowledge = null)
        {
            this.truth = truth ?? throw new ArgumentNullException(nameof(truth));
            this.viewingGangId = viewingGangId;
            this.knowledge = knowledge ?? FullTerritoryKnowledgeFilter.Instance;
            projector = new TerritoryPresentationProjector(
                profile ?? TerritoryPresentationProfile.Default);
        }

        public IReadOnlyList<TerritoryBlockId> BlockIds => truth.BlockIds;

        public bool TryGetBlock(
            TerritoryBlockId blockId, out TerritoryBlockPresentation presentation)
        {
            if (!truth.TryGetBlock(blockId, out var exact))
            {
                presentation = null;
                return false;
            }

            presentation = projector.Project(
                exact, knowledge.Observe(exact, viewingGangId), viewingGangId);
            return true;
        }
    }
}
