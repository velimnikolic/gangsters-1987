using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// Where a business stands with ONE family. Several of these can be true at once - a
    /// shop can be paying us and still be defiant towards the Falcones - so the state is
    /// per Business×Gang and never a single "owner" field on the premises.
    ///
    /// Deliberately extensible: Paying and MissedPayments belong to the economy epic and
    /// are not invented here, but nothing in this set has to be re-cut to admit them.
    /// </summary>
    public enum TerritoryProtectionState
    {
        /// <summary>The family has never spoken to this shop.</summary>
        Unaffiliated,

        /// <summary>Men have stood at the door; nothing has been asked yet.</summary>
        Approached,

        /// <summary>Asked, and the owner is wavering: not a yes, not a no.</summary>
        Hesitant,

        /// <summary>Leaned on since the last answer - the fear is fresh.</summary>
        Intimidated,

        /// <summary>Paying, in the only sense Phase 1 has: it accepts this family's word.</summary>
        Compliant,

        /// <summary>Told the family no, to its face.</summary>
        Defiant,
    }

    /// <summary>What an owner says when the demand is made.</summary>
    public enum TerritoryComplianceVerdict
    {
        Accept,
        Hesitate,
        Refuse,
    }

    /// <summary>What was done to a business, when a physical system resolves violence at it.</summary>
    public enum TerritoryEscalationKind
    {
        Assault,
        PropertyDamage,
    }

    /// <summary>One family's standing with one business, as the simulation holds it.</summary>
    public readonly struct TerritoryProtectionRelationship
    {
        public TerritoryProtectionRelationship(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            TerritoryProtectionState state,
            double stateSince,
            double lastInteraction,
            double refusedAt,
            int demands,
            int threats,
            int escalations)
        {
            BusinessId = businessId;
            GangId = gangId;
            State = state;
            StateSince = stateSince;
            LastInteraction = lastInteraction;
            RefusedAt = refusedAt;
            Demands = demands;
            Threats = threats;
            Escalations = escalations;
        }

        public TerritoryBusinessId BusinessId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryProtectionState State { get; }
        public double StateSince { get; }
        public double LastInteraction { get; }

        /// <summary>When the owner last said no to this family, in game hours. Negative
        /// when it never has. FEAR-010's window is measured from it.</summary>
        public double RefusedAt { get; }

        public int Demands { get; }
        public int Threats { get; }
        public int Escalations { get; }

        /// <summary>Paying in Phase-1 terms. Hesitation is NOT compliance.</summary>
        public bool IsProtected => State == TerritoryProtectionState.Compliant;
    }

    /// <summary>
    /// Every term an owner's answer was made of, with the weight each carried. The debug
    /// inspector prints it; the player never sees a single one of these numbers.
    /// </summary>
    public readonly struct TerritoryComplianceTerms
    {
        public TerritoryComplianceTerms(
            float fear,
            float presence,
            float trouble,
            float rivalPressure,
            float score,
            TerritoryComplianceVerdict verdict,
            float acceptAt = 0f)
        {
            Fear = fear;
            Presence = presence;
            Trouble = trouble;
            RivalPressure = rivalPressure;
            Score = score;
            Verdict = verdict;
            AcceptAt = acceptAt;
        }

        /// <summary>How much this street fears the family doing the asking.</summary>
        public float Fear { get; }

        /// <summary>How heavily that family stands on this street.</summary>
        public float Presence { get; }

        /// <summary>What the street has just been through, whoever did it - the reason a
        /// shop two doors from a shooting is easier to talk to (RACK-012).</summary>
        public float Trouble { get; }

        /// <summary>What the strongest other claim on this shop is worth - a rival's
        /// standing, and the current protector's if somebody else is already paid.</summary>
        public float RivalPressure { get; }

        public float Score { get; }
        public TerritoryComplianceVerdict Verdict { get; }

        /// <summary>
        /// What THIS man's yes actually costs - the table's bar plus his own nerve plus
        /// what his kind of place is worth (ECON-002/007). It is on the terms because a
        /// surface that prints the score without it tells the player a number he cannot
        /// use: a shop reading 34 was refusing because ITS bar was 43, and nothing on
        /// any screen said so.
        /// </summary>
        public float AcceptAt { get; }

        /// <summary>How far short of a yes he is, or 0 when he would say yes.</summary>
        public float Short => Math.Max(0f, AcceptAt - Score);
    }

    /// <summary>The block-side inputs an owner weighs, gathered by the caller.</summary>
    public readonly struct TerritoryComplianceInputs
    {
        public TerritoryComplianceInputs(
            float fearOfAsker,
            float presenceOfAsker,
            float blockTrouble,
            float strongestRival,
            float protectorStanding,
            bool alreadyProtectedByAsker)
        {
            FearOfAsker = fearOfAsker;
            PresenceOfAsker = presenceOfAsker;
            BlockTrouble = blockTrouble;
            StrongestRival = strongestRival;
            ProtectorStanding = protectorStanding;
            AlreadyProtectedByAsker = alreadyProtectedByAsker;
        }

        public float FearOfAsker { get; }
        public float PresenceOfAsker { get; }
        public float BlockTrouble { get; }
        public float StrongestRival { get; }

        /// <summary>What the family already being paid is worth here. Zero when nobody is.</summary>
        public float ProtectorStanding { get; }

        public bool AlreadyProtectedByAsker { get; }
    }

    /// <summary>
    /// Every number the racket is made of. One home for the tuning; no handler holds a
    /// constant of its own, and nothing here is random - the same street on the same day
    /// gives the same answer, which is what makes a refusal a fact the player can plan
    /// against rather than a dice roll.
    /// </summary>
    public sealed class TerritoryRacketConfig
    {
        public TerritoryRacketConfig(
            float fearWeight = 0.55f,
            float presenceWeight = 0.35f,
            float troubleWeight = 0.15f,
            float rivalWeight = 0.5f,
            float acceptAt = 30f,
            float hesitateAt = 16f,
            float hesitantComplianceShare = 0.35f,
            float switchMargin = 18f,
            int switchTicks = 3,
            float approachRadiusMetres = 14f,
            float threatSeverity = 1f,
            int historyPerBusiness = 16)
        {
            FearWeight = Math.Max(0f, fearWeight);
            PresenceWeight = Math.Max(0f, presenceWeight);
            TroubleWeight = Math.Max(0f, troubleWeight);
            RivalWeight = Math.Max(0f, rivalWeight);
            HesitateAt = hesitateAt;
            AcceptAt = Math.Max(hesitateAt, acceptAt);
            HesitantComplianceShare = Math.Min(1f, Math.Max(0f, hesitantComplianceShare));
            SwitchMargin = Math.Max(0f, switchMargin);
            SwitchTicks = Math.Max(1, switchTicks);
            ApproachRadiusMetres = Math.Max(1f, approachRadiusMetres);
            ThreatSeverity = Math.Max(0f, threatSeverity);
            HistoryPerBusiness = Math.Max(4, historyPerBusiness);
        }

        public float FearWeight { get; }
        public float PresenceWeight { get; }
        public float TroubleWeight { get; }

        /// <summary>How heavily another claim counts against the asking family.</summary>
        public float RivalWeight { get; }

        /// <summary>What a family must be worth on the street before an owner says yes.
        /// Lowered from 40 on the user's word (2026-09-01): at 40 the whole violence
        /// ladder - a threat is 3 points, a wrecked front 7, a robbery 13 - could not
        /// carry a demand on its own, and every shop stayed wavering while the boss ran
        /// out of things to do to it.</summary>
        public float AcceptAt { get; }
        public float HesitateAt { get; }

        /// <summary>What a wavering shop is worth when the block's compliance is counted.
        /// Never one: a maybe is not a yes.</summary>
        public float HesitantComplianceShare { get; }

        /// <summary>How far a challenger must be ahead of the family being paid.</summary>
        public float SwitchMargin { get; }

        /// <summary>And for how many consecutive business ticks, so one loud afternoon
        /// does not turn a street over.</summary>
        public int SwitchTicks { get; }

        /// <summary>How close a man must be standing to the door for the demand to be a
        /// real one. A click from across the city is an intent, not an interaction.</summary>
        public float ApproachRadiusMetres { get; }

        public float ThreatSeverity { get; }
        public int HistoryPerBusiness { get; }

        public static TerritoryRacketConfig Default { get; } = new TerritoryRacketConfig();
    }

    /// <summary>
    /// What an owner says, and why. Pure and deterministic: the same street, the same
    /// standing, the same answer - twice. Nothing here is a roll. The owner himself
    /// (ECON-002) and the tier guard (ECON-007) enter as THRESHOLD SHIFTS with neutral
    /// defaults, so every pre-economy evaluation reads exactly as it always did.
    /// </summary>
    public static class TerritoryComplianceEvaluation
    {
        public static TerritoryComplianceTerms Evaluate(
            TerritoryComplianceInputs inputs, TerritoryRacketConfig config) =>
            Evaluate(inputs, config, 0f, 0f);

        /// <param name="ownerShift">The owner's own nerve, in score points: positive
        /// for a man who takes more moving (Proud, Stubborn), negative for one who
        /// folds early (Cowardly). Zero is the neutral pre-ECON owner.</param>
        /// <param name="tierBar">The tier guard's addition to the ACCEPT threshold: a
        /// casino wants near everything a family can be before it pays. Zero for a
        /// tier-1 shopfront.</param>
        public static TerritoryComplianceTerms Evaluate(
            TerritoryComplianceInputs inputs, TerritoryRacketConfig config,
            float ownerShift, float tierBar)
        {
            config ??= TerritoryRacketConfig.Default;

            // The claim standing against the asker: another family's standing on the
            // street, or the one already being paid, whichever is worth more. A family
            // asking a shop it already protects is arguing with nobody.
            var opposing = Math.Max(0f, inputs.StrongestRival);
            if (!inputs.AlreadyProtectedByAsker)
                opposing = Math.Max(opposing, Math.Max(0f, inputs.ProtectorStanding));

            var score =
                config.FearWeight * Math.Max(0f, inputs.FearOfAsker) +
                config.PresenceWeight * Math.Max(0f, inputs.PresenceOfAsker) +
                config.TroubleWeight * Math.Max(0f, inputs.BlockTrouble) -
                config.RivalWeight * opposing;

            var acceptAt = config.AcceptAt + ownerShift + tierBar;
            var hesitateAt = config.HesitateAt + ownerShift + tierBar * 0.5f;
            var verdict = score >= acceptAt
                ? TerritoryComplianceVerdict.Accept
                : score >= hesitateAt
                    ? TerritoryComplianceVerdict.Hesitate
                    : TerritoryComplianceVerdict.Refuse;

            return new TerritoryComplianceTerms(
                inputs.FearOfAsker, inputs.PresenceOfAsker, inputs.BlockTrouble,
                opposing, score, verdict, acceptAt);
        }
    }

    /// <summary>
    /// What happened at one door, as a wire would carry it. A KIND, never a sentence:
    /// the strip over the street and the ledger's own telex both set the words from
    /// this, and nothing downstream ever reads English back into facts.
    /// </summary>
    public enum TerritoryDoorNews
    {
        /// <summary>The men stood at his door. Nothing asked yet.</summary>
        Approached,

        /// <summary>He said yes. The shop pays for peace.</summary>
        Agreed,

        /// <summary>Not a yes and not a no.</summary>
        Wavered,

        /// <summary>He said no, out loud, to men standing in his doorway.</summary>
        Refused,

        /// <summary>He was leaned on.</summary>
        Threatened,

        /// <summary>The front went in - smashed or burnt.</summary>
        Wrecked,

        /// <summary>Somebody at the premises was put on the ground.</summary>
        Beaten,

        /// <summary>The arrangement lapsed from HIS side: he stopped paying.</summary>
        StoppedPaying,

        /// <summary>Another family is being paid now.</summary>
        ChangedHands,

        // ---- money, not answers. What happened when somebody came to collect. ----

        /// <summary>He paid, and it was less than he owed.</summary>
        PaidShort,

        /// <summary>He did not pay at all this round.</summary>
        Missed,

        /// <summary>A round reached the front and the take went into the safe.</summary>
        RoundBanked,

        /// <summary>A round did not come home. The money went with the men.</summary>
        RoundLost,

        /// <summary>A standing round left on its own - the block's day came round and a
        /// man on the bag walked out to it without being told.</summary>
        RoundOut,
    }

    /// <summary>One line of door news, filed the hour it happened.</summary>
    public readonly struct TerritoryDoorDispatch
    {
        public TerritoryDoorDispatch(
            TerritoryBusinessId businessId, TerritoryGangId gangId,
            TerritoryDoorNews news, double gameHour)
            : this(businessId, gangId, news, gameHour, 0,
                TerritoryPaymentExcuse.None, default, 0, 0)
        {
        }

        /// <summary>The same slip with MONEY on it - what was paid or owed or carried,
        /// the story the owner told, and for a round the block it walked.</summary>
        public TerritoryDoorDispatch(
            TerritoryBusinessId businessId, TerritoryGangId gangId,
            TerritoryDoorNews news, double gameHour, int amount,
            TerritoryPaymentExcuse excuse, TerritoryBlockId blockId, int stops,
            int shortCount)
        {
            BusinessId = businessId;
            GangId = gangId;
            News = news;
            GameHour = gameHour;
            Amount = amount;
            Excuse = excuse;
            BlockId = blockId;
            Stops = stops;
            Short = shortCount;
        }

        /// <summary>Dollars: what he paid on a Short, what he owes on a Missed, what the
        /// round carried on the pair of Round slips. 0 on an answer.</summary>
        public int Amount { get; }

        /// <summary>The story the owner told, where he told one.</summary>
        public TerritoryPaymentExcuse Excuse { get; }

        /// <summary>The block a ROUND slip belongs to. Invalid on a door slip - a door
        /// knows its own block, and the wire looks it up.</summary>
        public TerritoryBlockId BlockId { get; }

        /// <summary>Doors on the round (Round slips), or what he owed (Short).</summary>
        public int Stops { get; }

        /// <summary>How many doors came up short or missed on the round.</summary>
        public int Short { get; }

        public TerritoryBusinessId BusinessId { get; }

        /// <summary>The family that did it, or was answered.</summary>
        public TerritoryGangId GangId { get; }

        public TerritoryDoorNews News { get; }
        public double GameHour { get; }

        /// <summary>
        /// The campaign day, counted the way the CAMPAIGN counts it.
        ///
        /// TWO CLOCKS, and they do not agree. GameHour is built off the city clock,
        /// whose Day is 0-BASED (TerritoryRuntime: clock.Day * 24 + clock.Hour), while
        /// an incident carries Campaign.Day, which is 1-BASED (OutfitDirector: today =
        /// clock.Day + 1). Without the +1 a door slip filed this afternoon prints
        /// yesterday's number and the wire files it UNDER yesterday's incidents, which
        /// is what it did until 2026-09-02.
        /// </summary>
        public int Day => (int)(GameHour / 24.0) + 1;

        /// <summary>The hour of that day, for a stamp that says WHEN as well as which
        /// day - a door answers at a time, and two slips on one day read in order.
        /// </summary>
        public double HourOfDay => GameHour - (int)(GameHour / 24.0) * 24.0;
    }

    /// <summary>One thing that happened between a family and a business.</summary>
    public readonly struct TerritoryRacketEntry
    {
        public TerritoryRacketEntry(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            string what,
            TerritoryProtectionState state,
            double gameHour,
            float score)
        {
            BusinessId = businessId;
            GangId = gangId;
            What = what ?? "";
            State = state;
            GameHour = gameHour;
            Score = score;
        }

        public TerritoryBusinessId BusinessId { get; }
        public TerritoryGangId GangId { get; }
        public string What { get; }
        public TerritoryProtectionState State { get; }
        public double GameHour { get; }
        public float Score { get; }
    }

    /// <summary>A relationship that moved; the runtime turns it into a compliance event.</summary>
    public readonly struct TerritoryProtectionChange
    {
        public TerritoryProtectionChange(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            TerritoryProtectionState previous,
            TerritoryProtectionState current,
            double gameHour)
        {
            BusinessId = businessId;
            GangId = gangId;
            Previous = previous;
            Current = current;
            GameHour = gameHour;
        }

        public TerritoryBusinessId BusinessId { get; }
        public TerritoryGangId GangId { get; }
        public TerritoryProtectionState Previous { get; }
        public TerritoryProtectionState Current { get; }
        public double GameHour { get; }
    }

    /// <summary>
    /// Who pays whom, shop by shop.
    ///
    /// The relationship hangs on the BUSINESS's identity, never on a marker or any other
    /// view: a shop's street is off camera most of the time, and it has to keep paying
    /// while nobody is looking at it. Nothing here assigns control of a block either -
    /// compliance is an INPUT the control reading takes, and a street full of shops paying
    /// us is not the same statement as a street we hold.
    ///
    /// Pure and time-free: the runtime supplies the game hour and the block-side numbers,
    /// and every family - ours and theirs - moves through exactly the same transitions.
    /// </summary>
    public sealed class TerritoryRacketLedger
    {
        readonly Dictionary<TerritoryBusinessId, BusinessRelations> businesses =
            new Dictionary<TerritoryBusinessId, BusinessRelations>();
        readonly List<TerritoryBusinessId> businessIds = new List<TerritoryBusinessId>();

        public TerritoryRacketLedger(TerritoryRacketConfig config = null) =>
            Config = config ?? TerritoryRacketConfig.Default;

        public TerritoryRacketConfig Config { get; set; }

        /// <summary>Every business any family has ever spoken to.</summary>
        public IReadOnlyList<TerritoryBusinessId> Businesses => businessIds;

        public TerritoryProtectionState StateOf(
            TerritoryBusinessId businessId, TerritoryGangId gangId) =>
            businesses.TryGetValue(businessId, out var row)
                ? row.StateOf(gangId)
                : TerritoryProtectionState.Unaffiliated;

        public bool TryGetRelationship(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            out TerritoryProtectionRelationship relationship)
        {
            relationship = default;
            return businesses.TryGetValue(businessId, out var row) &&
                   row.TryGet(gangId, out relationship);
        }

        /// <summary>The family this shop is actually paying, if any.</summary>
        public bool TryGetProtector(TerritoryBusinessId businessId, out TerritoryGangId gangId)
        {
            gangId = default;
            return businesses.TryGetValue(businessId, out var row) && row.TryGetProtector(out gangId);
        }

        public void CollectRelationships(
            TerritoryBusinessId businessId, List<TerritoryProtectionRelationship> into)
        {
            into?.Clear();
            if (into != null && businesses.TryGetValue(businessId, out var row))
                row.Collect(into);
        }

        /// <summary>What has passed between this shop and everyone, newest first.</summary>
        public void CollectHistory(
            TerritoryBusinessId businessId, List<TerritoryRacketEntry> into)
        {
            into?.Clear();
            if (into != null && businesses.TryGetValue(businessId, out var row))
                row.CollectHistory(into);
        }

        /// <summary>
        /// How many lines of door news the ledger keeps.
        ///
        /// It WAS 24 - a wire being a strip of the last few hours and the per-business
        /// history being the archive. The ledger's rail now stands the whole wire on end
        /// and scrolls it, so the strip is the archive as far as a boss is concerned, and
        /// 24 lines was an afternoon of a working racket. Raised on the user's word,
        /// 2026-09-02. The strip over the street still shows the newest few of them.
        /// </summary>
        public const int DispatchesKept = 1000;

        readonly List<TerritoryDoorDispatch> dispatches = new List<TerritoryDoorDispatch>();

        /// <summary>The city's door news, oldest first. Every surface that carries a wire
        /// reads THIS - the strip over the street and the book's own telex - so the two
        /// can never report different nights.</summary>
        public IReadOnlyList<TerritoryDoorDispatch> Dispatches => dispatches;

        void FileEscalation(
            TerritoryBusinessId businessId, TerritoryGangId gangId,
            TerritoryEscalationKind kind, double gameHour) =>
            File(businessId, gangId,
                kind == TerritoryEscalationKind.Assault
                    ? TerritoryDoorNews.Beaten
                    : TerritoryDoorNews.Wrecked,
                gameHour);

        /// <summary>Files one line and moves the version the surfaces watch.</summary>
        void File(
            TerritoryBusinessId businessId, TerritoryGangId gangId,
            TerritoryDoorNews news, double gameHour)
        {
            if (dispatches.Count >= DispatchesKept)
                dispatches.RemoveAt(0);
            dispatches.Add(new TerritoryDoorDispatch(businessId, gangId, news, gameHour));
            Version++;
        }

        /// <summary>
        /// MONEY AT ONE DOOR. What a man came away with when he went to collect - short,
        /// or nothing at all - with the sum and the story the owner told.
        ///
        /// A door that pays in full files NOTHING: it is the arrangement working, and one
        /// slip per paying door per week would bury the wire in good news.
        /// </summary>
        public void FileMoney(
            TerritoryBusinessId businessId, TerritoryGangId gangId,
            TerritoryDoorNews news, double gameHour, int amount, int owed,
            TerritoryPaymentExcuse excuse)
        {
            if (dispatches.Count >= DispatchesKept)
                dispatches.RemoveAt(0);
            dispatches.Add(new TerritoryDoorDispatch(
                businessId, gangId, news, gameHour, amount, excuse, default, owed, 0));
            Version++;
        }

        /// <summary>MONEY OFF A WHOLE BLOCK: a round banked at the front, or lost with
        /// the men who were carrying it.</summary>
        public void FileRound(
            TerritoryBlockId blockId, TerritoryGangId gangId,
            TerritoryDoorNews news, double gameHour, int amount, int stops,
            int shortCount)
        {
            if (dispatches.Count >= DispatchesKept)
                dispatches.RemoveAt(0);
            dispatches.Add(new TerritoryDoorDispatch(
                default, gangId, news, gameHour, amount,
                TerritoryPaymentExcuse.None, blockId, stops, shortCount));
            Version++;
        }

        /// <summary>
        /// Moves on every interaction the ledger records. A SURFACE reads this to know
        /// its painted sheet is stale: the block's compliance figures do not move when a
        /// shop goes from wavering to shaken (both count as the same fraction of a yes),
        /// so a smashed front left the open block file showing the line it was painted
        /// with - "the owner is wavering" over a boarded shop.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>Men stood at the door. Nothing has been asked, and nothing is owed.</summary>
        /// <param name="announce">Whether the standing itself is worth a slip. FALSE
        /// for a walk that carries a question: the men arriving and the owner's answer
        /// are ONE thing that happened at that door, and filing both put two lines on
        /// the wire seconds apart for one visit. The state still moves either way.
        /// </param>
        public bool Approach(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            double gameHour,
            List<TerritoryProtectionChange> changes = null,
            bool announce = true)
        {
            if (!businessId.IsValid || !gangId.IsValid)
                return false;

            var row = Row(businessId);
            var entry = row.Entry(gangId);
            entry.LastInteraction = gameHour;
            // Standing at the door of a shop that already pays you, or has already told
            // you no, does not undo either of those.
            if (entry.State != TerritoryProtectionState.Unaffiliated)
                return false;

            row.Move(entry, TerritoryProtectionState.Approached, gameHour, "approached", 0f, Config, changes);
            if (announce)
                File(businessId, gangId, TerritoryDoorNews.Approached, gameHour);
            return true;
        }

        /// <summary>
        /// The demand is made, and the owner answers. This is the only door into
        /// compliance: there is no path that sets a shop to paying without an answer.
        /// </summary>
        public TerritoryComplianceVerdict Demand(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            TerritoryComplianceInputs inputs,
            double gameHour,
            out TerritoryComplianceTerms terms,
            List<TerritoryProtectionChange> changes = null,
            float ownerShift = 0f,
            float tierBar = 0f)
        {
            terms = TerritoryComplianceEvaluation.Evaluate(inputs, Config, ownerShift, tierBar);
            if (!businessId.IsValid || !gangId.IsValid)
                return terms.Verdict;

            var row = Row(businessId);
            var entry = row.Entry(gangId);
            entry.Demands++;
            entry.LastInteraction = gameHour;

            switch (terms.Verdict)
            {
                case TerritoryComplianceVerdict.Accept:
                    row.Move(entry, TerritoryProtectionState.Compliant, gameHour,
                        "accepted", terms.Score, Config, changes);
                    break;
                case TerritoryComplianceVerdict.Hesitate:
                    row.Move(entry, TerritoryProtectionState.Hesitant, gameHour,
                        "hesitated", terms.Score, Config, changes);
                    break;
                default:
                    entry.RefusedAt = gameHour;
                    row.Move(entry, TerritoryProtectionState.Defiant, gameHour,
                        "refused", terms.Score, Config, changes);
                    break;
            }

            File(businessId, gangId,
                terms.Verdict == TerritoryComplianceVerdict.Accept
                    ? TerritoryDoorNews.Agreed
                    : terms.Verdict == TerritoryComplianceVerdict.Hesitate
                        ? TerritoryDoorNews.Wavered
                        : TerritoryDoorNews.Refused,
                gameHour);
            return terms.Verdict;
        }

        /// <summary>
        /// The owner is leaned on. The threat itself is a Fear act the caller records;
        /// here it only marks the shop as freshly frightened and lets the answer be asked
        /// for again.
        /// </summary>
        public void Threaten(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            double gameHour,
            List<TerritoryProtectionChange> changes = null)
        {
            if (!businessId.IsValid || !gangId.IsValid)
                return;

            var row = Row(businessId);
            var entry = row.Entry(gangId);
            entry.Threats++;
            entry.LastInteraction = gameHour;
            // A shop already paying is not "intimidated" back down the ladder.
            if (entry.State == TerritoryProtectionState.Compliant)
            {
                row.Note(entry, "threatened while paying", gameHour, 0f, Config);
                File(businessId, gangId, TerritoryDoorNews.Threatened, gameHour);
                return;
            }

            row.Move(entry, TerritoryProtectionState.Intimidated, gameHour,
                "threatened", 0f, Config, changes);
            File(businessId, gangId, TerritoryDoorNews.Threatened, gameHour);
        }

        /// <summary>
        /// Violence landed on the premises and a physical system resolved it. The racket
        /// records that it happened; the Fear it causes is the caller's to file, because
        /// Fear has one owner and it is not this ledger.
        /// </summary>
        public void Escalate(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            TerritoryEscalationKind kind,
            double gameHour,
            List<TerritoryProtectionChange> changes = null)
        {
            if (!businessId.IsValid || !gangId.IsValid)
                return;

            var row = Row(businessId);
            var entry = row.Entry(gangId);
            entry.Escalations++;
            entry.LastInteraction = gameHour;
            if (entry.State == TerritoryProtectionState.Compliant)
            {
                row.Note(entry, kind + " against a paying shop", gameHour, 0f, Config);
                FileEscalation(businessId, gangId, kind, gameHour);
                return;
            }

            row.Move(entry, TerritoryProtectionState.Intimidated, gameHour,
                kind.ToString().ToLowerInvariant(), 0f, Config, changes);
            FileEscalation(businessId, gangId, kind, gameHour);
        }

        /// <summary>
        /// The arrangement lapsed from the OWNER's side (ECON-003): two missed
        /// collections running and nobody answered it. The shop slides back to
        /// Hesitant - he has not defied anybody, he has just stopped paying a family
        /// that stopped collecting like it meant it.
        /// </summary>
        public bool Lapse(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            double gameHour,
            List<TerritoryProtectionChange> changes = null)
        {
            if (!businesses.TryGetValue(businessId, out var row) || !gangId.IsValid)
                return false;
            var entry = row.Entry(gangId);
            if (entry.State != TerritoryProtectionState.Compliant)
                return false;

            row.Move(entry, TerritoryProtectionState.Hesitant, gameHour,
                "stopped paying", 0f, Config, changes);
            File(businessId, gangId, TerritoryDoorNews.StoppedPaying, gameHour);
            return true;
        }

        /// <summary>
        /// A challenger has been worth more than the family being paid for long enough.
        /// The shop changes hands - and that is all it does: no block changes hands with it.
        /// </summary>
        public bool Switch(
            TerritoryBusinessId businessId,
            TerritoryGangId challenger,
            double gameHour,
            List<TerritoryProtectionChange> changes = null)
        {
            if (!businesses.TryGetValue(businessId, out var row) || !challenger.IsValid)
                return false;
            if (!row.TryGetProtector(out var incumbent) || incumbent == challenger)
                return false;

            row.Move(row.Entry(incumbent), TerritoryProtectionState.Unaffiliated, gameHour,
                "lost the shop", 0f, Config, changes);
            row.Move(row.Entry(challenger), TerritoryProtectionState.Compliant, gameHour,
                "took the shop", 0f, Config, changes);
            File(businessId, challenger, TerritoryDoorNews.ChangedHands, gameHour);
            return true;
        }

        /// <summary>
        /// How many consecutive business ticks this challenger has been far enough ahead.
        /// Kept per business so a single loud afternoon cannot turn a street over.
        /// </summary>
        public int PressTowardSwitch(
            TerritoryBusinessId businessId, TerritoryGangId challenger, bool ahead)
        {
            var row = Row(businessId);
            if (!ahead || !challenger.IsValid)
            {
                row.PressureGang = default;
                row.PressureTicks = 0;
                return 0;
            }

            if (row.PressureGang != challenger)
            {
                row.PressureGang = challenger;
                row.PressureTicks = 0;
            }

            row.PressureTicks++;
            return row.PressureTicks;
        }

        /// <summary>
        /// What a block's businesses add up to: how many are paying somebody, and the
        /// share of the street that is - counting a wavering shop as the fraction of a
        /// yes the config says it is, never as a whole one.
        /// </summary>
        public void Compliance(
            IReadOnlyList<TerritoryBusinessId> blockBusinesses,
            out int compliant,
            out int total,
            out float share)
        {
            compliant = 0;
            total = 0;
            share = 0f;
            if (blockBusinesses == null || blockBusinesses.Count == 0)
                return;

            var weighted = 0f;
            total = blockBusinesses.Count;
            for (var i = 0; i < blockBusinesses.Count; i++)
            {
                if (!businesses.TryGetValue(blockBusinesses[i], out var row))
                    continue;
                if (row.TryGetProtector(out _))
                {
                    compliant++;
                    weighted += 1f;
                }
                else if (row.HasHesitant)
                {
                    weighted += Config.HesitantComplianceShare;
                }
            }

            share = weighted * 100f / total;
        }

        /// <summary>
        /// How much of this street is paying ONE family, on the same scale the block's
        /// overall compliance is counted in. A wavering shop counts as the configured
        /// fraction of a yes to that family, and to nobody else.
        /// </summary>
        public float ComplianceOf(
            IReadOnlyList<TerritoryBusinessId> blockBusinesses, TerritoryGangId gangId)
        {
            if (blockBusinesses == null || blockBusinesses.Count == 0 || !gangId.IsValid)
                return 0f;

            var weighted = 0f;
            for (var i = 0; i < blockBusinesses.Count; i++)
            {
                if (!businesses.TryGetValue(blockBusinesses[i], out var row))
                    continue;
                var state = row.StateOf(gangId);
                if (state == TerritoryProtectionState.Compliant)
                    weighted += 1f;
                else if (state == TerritoryProtectionState.Hesitant ||
                         state == TerritoryProtectionState.Intimidated)
                    weighted += Config.HesitantComplianceShare;
            }

            return weighted * 100f / blockBusinesses.Count;
        }

        /// <summary>Every family with any standing at all on this street's shops.</summary>
        public void CollectGangsOn(
            IReadOnlyList<TerritoryBusinessId> blockBusinesses, List<TerritoryGangId> into)
        {
            into?.Clear();
            if (into == null || blockBusinesses == null)
                return;

            for (var i = 0; i < blockBusinesses.Count; i++)
            {
                if (!businesses.TryGetValue(blockBusinesses[i], out var row))
                    continue;
                row.CollectGangs(into);
            }
        }

        /// <summary>
        /// EVERY DOOR'S STANDING WITH EVERY FAMILY, flat (RIVAL-010). The wire's own
        /// dispatches are NOT collected: a slip is news, and news a week old that
        /// reappears at load would print yesterday's afternoon over this one's.
        /// </summary>
        public void Collect(List<ProtectionRowDto> into)
        {
            if (into == null)
                return;
            into.Clear();
            for (var i = 0; i < businessIds.Count; i++)
            {
                if (!businesses.TryGetValue(businessIds[i], out var row))
                    continue;
                row.CollectRows(businessIds[i], into);
            }
        }

        /// <summary>The load boundary. Every standing the ledger held is replaced.
        /// </summary>
        public void RestoreFrom(ProtectionRowDto[] rows)
        {
            businesses.Clear();
            businessIds.Clear();
            dispatches.Clear();

            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var dto = rows[i];
                var businessId = new TerritoryBusinessId(dto.businessId);
                if (!businessId.IsValid)
                    continue;

                var entry = Row(businessId).Entry(new TerritoryGangId(dto.gangId));
                entry.State = (TerritoryProtectionState)dto.state;
                entry.StateSince = dto.stateSince;
                entry.LastInteraction = dto.lastInteraction;
                entry.RefusedAt = dto.refusedAt;
                entry.Demands = dto.demands;
                entry.Threats = dto.threats;
                entry.Escalations = dto.escalations;
            }
            Version++;
        }

        BusinessRelations Row(TerritoryBusinessId businessId)
        {
            if (businesses.TryGetValue(businessId, out var row))
                return row;
            row = new BusinessRelations(businessId);
            businesses.Add(businessId, row);
            businessIds.Add(businessId);
            return row;
        }

        sealed class BusinessRelations
        {
            readonly List<GangEntry> gangs = new List<GangEntry>();
            readonly List<TerritoryRacketEntry> history = new List<TerritoryRacketEntry>();

            public BusinessRelations(TerritoryBusinessId businessId) => BusinessId = businessId;

            public void CollectRows(
                TerritoryBusinessId businessId, List<ProtectionRowDto> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    var entry = gangs[i];
                    into.Add(new ProtectionRowDto
                    {
                        businessId = businessId.Value,
                        gangId = entry.GangId.Value,
                        state = (int)entry.State,
                        stateSince = entry.StateSince,
                        lastInteraction = entry.LastInteraction,
                        refusedAt = entry.RefusedAt,
                        demands = entry.Demands,
                        threats = entry.Threats,
                        escalations = entry.Escalations,
                    });
                }
            }

            public TerritoryBusinessId BusinessId { get; }
            public TerritoryGangId PressureGang { get; set; }
            public int PressureTicks { get; set; }

            public bool HasHesitant
            {
                get
                {
                    for (var i = 0; i < gangs.Count; i++)
                        if (gangs[i].State == TerritoryProtectionState.Hesitant ||
                            gangs[i].State == TerritoryProtectionState.Intimidated)
                            return true;
                    return false;
                }
            }

            /// <summary>Ascending by gang id, so two identical situations read alike.</summary>
            public GangEntry Entry(TerritoryGangId gangId)
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

            public TerritoryProtectionState StateOf(TerritoryGangId gangId)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].GangId == gangId)
                        return gangs[i].State;
                return TerritoryProtectionState.Unaffiliated;
            }

            public bool TryGet(
                TerritoryGangId gangId, out TerritoryProtectionRelationship relationship)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    if (gangs[i].GangId != gangId)
                        continue;
                    relationship = gangs[i].Snapshot(BusinessId);
                    return true;
                }

                relationship = default;
                return false;
            }

            /// <summary>Only one family can be paid at a time; the first in gang order is
            /// the answer, and the transitions are what keep it to one.</summary>
            public bool TryGetProtector(out TerritoryGangId gangId)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    if (gangs[i].State != TerritoryProtectionState.Compliant)
                        continue;
                    gangId = gangs[i].GangId;
                    return true;
                }

                gangId = default;
                return false;
            }

            public void CollectGangs(List<TerritoryGangId> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].State != TerritoryProtectionState.Unaffiliated &&
                        !into.Contains(gangs[i].GangId))
                        into.Add(gangs[i].GangId);
            }

            public void Collect(List<TerritoryProtectionRelationship> into)
            {
                for (var i = 0; i < gangs.Count; i++)
                    into.Add(gangs[i].Snapshot(BusinessId));
            }

            public void CollectHistory(List<TerritoryRacketEntry> into)
            {
                for (var i = history.Count - 1; i >= 0; i--)
                    into.Add(history[i]);
            }

            public void Move(
                GangEntry entry,
                TerritoryProtectionState state,
                double gameHour,
                string what,
                float score,
                TerritoryRacketConfig config,
                List<TerritoryProtectionChange> changes)
            {
                var previous = entry.State;
                if (state == TerritoryProtectionState.Compliant)
                    ClearOtherProtectors(entry, gameHour, config, changes);

                if (previous != state)
                {
                    entry.State = state;
                    entry.StateSince = gameHour;
                    changes?.Add(new TerritoryProtectionChange(
                        BusinessId, entry.GangId, previous, state, gameHour));
                }

                Note(entry, what, gameHour, score, config);
            }

            public void Note(
                GangEntry entry, string what, double gameHour, float score,
                TerritoryRacketConfig config)
            {
                if (history.Count >= config.HistoryPerBusiness)
                    history.RemoveAt(0);
                history.Add(new TerritoryRacketEntry(
                    BusinessId, entry.GangId, what, entry.State, gameHour, score));
            }

            /// <summary>A shop pays one family. Accepting a new one ends the old
            /// arrangement rather than quietly running two.</summary>
            void ClearOtherProtectors(
                GangEntry taking,
                double gameHour,
                TerritoryRacketConfig config,
                List<TerritoryProtectionChange> changes)
            {
                for (var i = 0; i < gangs.Count; i++)
                {
                    var other = gangs[i];
                    if (other == taking || other.State != TerritoryProtectionState.Compliant)
                        continue;
                    var previous = other.State;
                    other.State = TerritoryProtectionState.Unaffiliated;
                    other.StateSince = gameHour;
                    changes?.Add(new TerritoryProtectionChange(
                        BusinessId, other.GangId, previous,
                        TerritoryProtectionState.Unaffiliated, gameHour));
                    Note(other, "lost the shop", gameHour, 0f, config);
                }
            }
        }

        sealed class GangEntry
        {
            public GangEntry(TerritoryGangId gangId)
            {
                GangId = gangId;
                RefusedAt = -1.0;
            }

            public TerritoryGangId GangId { get; }
            public TerritoryProtectionState State { get; set; } =
                TerritoryProtectionState.Unaffiliated;
            public double StateSince { get; set; }
            public double LastInteraction { get; set; }
            public double RefusedAt { get; set; }
            public int Demands { get; set; }
            public int Threats { get; set; }
            public int Escalations { get; set; }

            public TerritoryProtectionRelationship Snapshot(TerritoryBusinessId businessId) =>
                new TerritoryProtectionRelationship(
                    businessId, GangId, State, StateSince, LastInteraction, RefusedAt,
                    Demands, Threats, Escalations);
        }
    }
}
