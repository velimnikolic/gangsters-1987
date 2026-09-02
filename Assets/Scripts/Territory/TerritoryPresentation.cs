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
            TerritoryOwnerTone ownerTone = TerritoryOwnerTone.Unknown,
            string answersForIt = "")
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
            AnswersForIt = answersForIt ?? "";
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

        /// <summary>
        /// Who in OUR outfit answers for this street (UI-001), by name - the lieutenant
        /// it is on the paper of, or the Boss where it is on his own. Empty where nobody
        /// has been made responsible for it.
        ///
        /// Responsibility is a different claim from control and a page prints it as one:
        /// a street can be somebody's to answer for and nobody's to hold. It is our own
        /// paperwork and never a rival's - another family's chain of command is not
        /// something the player's map has any way of knowing.
        /// </summary>
        public string AnswersForIt { get; }
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
            bool hasRecentTrouble,
            string paysLine = "",
            string statusLine = "")
        {
            BusinessId = businessId;
            BusinessName = businessName ?? "";
            BlockName = blockName ?? "";
            Standing = standing ?? "Unknown";
            Protector = protector ?? "";
            LocalSituation = localSituation ?? "Unknown";
            OwnerTone = ownerTone;
            HasRecentTrouble = hasRecentTrouble;
            PaysLine = paysLine ?? "";
            StatusLine = statusLine ?? "";
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

        /// <summary>What the place pays and when it last did (ECON-008), pre-rendered
        /// as words. Empty for a shop that pays the viewer nothing.</summary>
        public string PaysLine { get; }

        /// <summary>Temporary operational condition and recovery time, already reduced
        /// to words by the authoritative business simulation.</summary>
        public string StatusLine { get; }
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

        /// <summary>
        /// One line of door news as the wire sets it. The strip over the street and the
        /// book's own telex both print THIS - 1987 wire brevity, the shop named, no
        /// adjective the machine would have cut - so a boss on the map and a boss with
        /// the book open are told the same thing in the same words.
        /// </summary>
        public string Describe(TerritoryDoorNews news, string shop)
        {
            var name = string.IsNullOrWhiteSpace(shop) ? "A SHOP" : shop.ToUpperInvariant();
            switch (news)
            {
                case TerritoryDoorNews.Approached:
                    return "OUR MEN ARE STANDING IN THE DOOR OF " + name;
                case TerritoryDoorNews.Agreed:
                    return name + " PAYS US FROM TODAY";
                case TerritoryDoorNews.Wavered:
                    return "THE OWNER OF " + name + " IS WAVERING - HE HAS NOT SAID NO";
                case TerritoryDoorNews.Refused:
                    return "THE OWNER OF " + name + " REFUSED US";
                case TerritoryDoorNews.Threatened:
                    return "WE LEANED ON THE OWNER OF " + name;
                case TerritoryDoorNews.Wrecked:
                    return "THE FRONT OF " + name + " WENT IN";
                case TerritoryDoorNews.Beaten:
                    return "SOMEBODY WAS PUT ON THE GROUND AT " + name;
                case TerritoryDoorNews.StoppedPaying:
                    return name + " HAS STOPPED PAYING US";
                default:
                    return name + " PAYS SOMEBODY ELSE NOW";
            }
        }

        /// <summary>
        /// The same wire, for a slip that carries MONEY. The money kinds need the sum
        /// and the story with them, which the news alone cannot say - everything else
        /// falls through to the plain reading above.
        /// </summary>
        public string Describe(TerritoryDoorDispatch dispatch, string shop, string block)
        {
            var name = string.IsNullOrWhiteSpace(shop) ? "A SHOP" : shop.ToUpperInvariant();
            var where = string.IsNullOrWhiteSpace(block)
                ? "THE BLOCK"
                : block.ToUpperInvariant();
            var excuse = ExcuseWord(dispatch.Excuse);
            switch (dispatch.News)
            {
                case TerritoryDoorNews.PaidShort:
                    return name + " CAME UP SHORT - $" + dispatch.Amount + " OF $" +
                           dispatch.Stops + (excuse.Length > 0 ? " \u00b7 " + excuse : "");
                case TerritoryDoorNews.Missed:
                    return name + " DID NOT PAY - $" + dispatch.Amount + " OWED" +
                           (excuse.Length > 0 ? " \u00b7 " + excuse : "");
                case TerritoryDoorNews.RoundBanked:
                    return "THE ROUND ON " + where + " BANKED $" + dispatch.Amount +
                           " \u00b7 " + dispatch.Stops + " DOORS, " + dispatch.Short + " SHORT";
                case TerritoryDoorNews.RoundLost:
                    return "THE ROUND ON " + where + " IS GONE - $" + dispatch.Amount +
                           " LOST WITH THE MEN";
                case TerritoryDoorNews.RoundOut:
                    return "THE ROUND ON " + where + " IS OUT - " + dispatch.Stops +
                           " DOORS, $" + dispatch.Amount + " OWED";
                default:
                    return Describe(dispatch.News, shop);
            }
        }

        /// <summary>
        /// The story, as the owner tells it. Whether it is true is never printed - a crew
        /// that knows its street knows. ONE copy, so the toast the street shows and the
        /// slip the wire files cannot word the same excuse differently.
        /// </summary>
        public static string ExcuseWord(TerritoryPaymentExcuse excuse)
        {
            switch (excuse)
            {
                case TerritoryPaymentExcuse.BadWeek: return "\"A BAD WEEK\"";
                case TerritoryPaymentExcuse.WasRobbed: return "\"WE WERE ROBBED\"";
                case TerritoryPaymentExcuse.PoliceWereRound: return "\"THE POLICE WERE ROUND\"";
                default: return "";
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

            // Our OWN numbers are never unknown to us: a street we have nobody on reads
            // None, not Unknown. The knowledge filter is about what we can see of other
            // houses, and it has never been about what we can see of ourselves.
            float? ownPresence = 0f;
            float? ownHolding = 0f;
            float? ownFear = 0f;
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
                Tone(ownFear),
                AnswersFor(truth.Responsibility, viewingGangId));
        }

        /// <summary>
        /// The name on the paper for this street, or nothing. Ours only: a rival's
        /// command chain is not knowledge the player has, and the presentation layer is
        /// exactly where that line is drawn.
        /// </summary>
        static string AnswersFor(
            TerritoryResponsibilityView view, TerritoryGangId viewingGangId)
        {
            var responsibility = view.Responsibility;
            if (!responsibility.IsAssigned)
                return "";
            if (responsibility.GangId.IsValid && responsibility.GangId != viewingGangId)
                return "";
            if (view.LieutenantName.Length > 0)
                return view.LieutenantName;
            return view.BossName.Length > 0 ? view.BossName : "assigned";
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
