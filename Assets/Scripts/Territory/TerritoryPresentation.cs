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
            string noneLabel = "None",
            string weakLabel = "Weak",
            string moderateLabel = "Moderate",
            string strongLabel = "Strong",
            string unknownLabel = "Unknown")
        {
            if (weakAt > moderateAt || moderateAt > strongAt)
                throw new ArgumentException("Territory presentation thresholds must be ascending.");

            WeakAt = weakAt;
            ModerateAt = moderateAt;
            StrongAt = strongAt;
            NoneLabel = noneLabel ?? "None";
            WeakLabel = weakLabel ?? "Weak";
            ModerateLabel = moderateLabel ?? "Moderate";
            StrongLabel = strongLabel ?? "Strong";
            UnknownLabel = unknownLabel ?? "Unknown";
        }

        public float WeakAt { get; }
        public float ModerateAt { get; }
        public float StrongAt { get; }
        public string NoneLabel { get; }
        public string WeakLabel { get; }
        public string ModerateLabel { get; }
        public string StrongLabel { get; }
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
            return StrongLabel;
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
            string uncontrolled = "Uncontrolled",
            string influenced = "Influenced",
            string contested = "Contested",
            string controlled = "Controlled",
            string unknownControl = "Unknown")
        {
            Presence = presence ?? throw new ArgumentNullException(nameof(presence));
            Fear = fear ?? throw new ArgumentNullException(nameof(fear));
            Compliance = compliance ?? throw new ArgumentNullException(nameof(compliance));
            RivalActivity = rivalActivity ?? throw new ArgumentNullException(nameof(rivalActivity));
            Uncontrolled = uncontrolled ?? "Uncontrolled";
            Influenced = influenced ?? "Influenced";
            Contested = contested ?? "Contested";
            Controlled = controlled ?? "Controlled";
            UnknownControl = unknownControl ?? "Unknown";
        }

        public TerritoryQualitativeScale Presence { get; }
        public TerritoryQualitativeScale Fear { get; }
        public TerritoryQualitativeScale Compliance { get; }
        public TerritoryQualitativeScale RivalActivity { get; }
        public string Uncontrolled { get; }
        public string Influenced { get; }
        public string Contested { get; }
        public string Controlled { get; }
        public string UnknownControl { get; }

        public static TerritoryPresentationProfile Default { get; } =
            new TerritoryPresentationProfile(
                new TerritoryQualitativeScale(0.01f, 25f, 60f),
                new TerritoryQualitativeScale(0.01f, 25f, 60f),
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
            string control)
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
        }

        public TerritoryBlockId BlockId { get; }
        public string BlockName { get; }
        public TerritoryNeighborhoodId NeighborhoodId { get; }
        public string NeighborhoodName { get; }
        public string Presence { get; }
        public string LocalFear { get; }
        public string Businesses { get; }
        public string RivalActivity { get; }
        public string Control { get; }
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
            float? strongestRival = null;
            for (var i = 0; i < observed.Gangs.Count; i++)
            {
                var gang = observed.Gangs[i];
                if (gang.GangId == viewingGangId)
                {
                    ownPresence = gang.Presence;
                    continue;
                }

                if (!strongestRival.HasValue || gang.Influence > strongestRival.Value)
                    strongestRival = gang.Influence;
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
                ControlLabel(observed.Control));
        }

        string ControlLabel(TerritoryControlState control)
        {
            switch (control)
            {
                case TerritoryControlState.Uncontrolled: return profile.Uncontrolled;
                case TerritoryControlState.Influenced: return profile.Influenced;
                case TerritoryControlState.Contested: return profile.Contested;
                case TerritoryControlState.Controlled: return profile.Controlled;
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
