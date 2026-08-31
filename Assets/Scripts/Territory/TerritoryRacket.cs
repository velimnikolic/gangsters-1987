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
            TerritoryComplianceVerdict verdict)
        {
            Fear = fear;
            Presence = presence;
            Trouble = trouble;
            RivalPressure = rivalPressure;
            Score = score;
            Verdict = verdict;
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
            float acceptAt = 40f,
            float hesitateAt = 16f,
            float hesitantComplianceShare = 0.35f,
            float switchMargin = 18f,
            int switchTicks = 3,
            float rivalDemandPresence = 25f,
            int rivalDemandsPerTick = 2,
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
            RivalDemandPresence = Math.Max(0f, rivalDemandPresence);
            RivalDemandsPerTick = Math.Max(0, rivalDemandsPerTick);
            ApproachRadiusMetres = Math.Max(1f, approachRadiusMetres);
            ThreatSeverity = Math.Max(0f, threatSeverity);
            HistoryPerBusiness = Math.Max(4, historyPerBusiness);
        }

        public float FearWeight { get; }
        public float PresenceWeight { get; }
        public float TroubleWeight { get; }

        /// <summary>How heavily another claim counts against the asking family.</summary>
        public float RivalWeight { get; }

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

        /// <summary>The Presence a family needs on a block before it starts leaning on the
        /// shops there of its own accord.</summary>
        public float RivalDemandPresence { get; }

        public int RivalDemandsPerTick { get; }

        /// <summary>How close a man must be standing to the door for the demand to be a
        /// real one. A click from across the city is an intent, not an interaction.</summary>
        public float ApproachRadiusMetres { get; }

        public float ThreatSeverity { get; }
        public int HistoryPerBusiness { get; }

        public static TerritoryRacketConfig Default { get; } = new TerritoryRacketConfig();
    }

    /// <summary>
    /// What an owner says, and why. Pure and deterministic: the same street, the same
    /// standing, the same answer - twice. Owner personality belongs to a later epic and is
    /// deliberately absent, so nothing here is a roll.
    /// </summary>
    public static class TerritoryComplianceEvaluation
    {
        public static TerritoryComplianceTerms Evaluate(
            TerritoryComplianceInputs inputs, TerritoryRacketConfig config)
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

            var verdict = score >= config.AcceptAt
                ? TerritoryComplianceVerdict.Accept
                : score >= config.HesitateAt
                    ? TerritoryComplianceVerdict.Hesitate
                    : TerritoryComplianceVerdict.Refuse;

            return new TerritoryComplianceTerms(
                inputs.FearOfAsker, inputs.PresenceOfAsker, inputs.BlockTrouble,
                opposing, score, verdict);
        }
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

        /// <summary>Men stood at the door. Nothing has been asked, and nothing is owed.</summary>
        public bool Approach(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            double gameHour,
            List<TerritoryProtectionChange> changes = null)
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
            List<TerritoryProtectionChange> changes = null)
        {
            terms = TerritoryComplianceEvaluation.Evaluate(inputs, Config);
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
                return;
            }

            row.Move(entry, TerritoryProtectionState.Intimidated, gameHour,
                "threatened", 0f, Config, changes);
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
                return;
            }

            row.Move(entry, TerritoryProtectionState.Intimidated, gameHour,
                kind.ToString().ToLowerInvariant(), 0f, Config, changes);
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
