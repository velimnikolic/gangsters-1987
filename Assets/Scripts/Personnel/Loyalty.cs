using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// Who a man is loyal TO, and what moves it.
    ///
    /// <see cref="Character.Loyalty"/> is not loyalty to the outfit. It is loyalty to
    /// the one man he actually answers to - a hood to his lieutenant, a lieutenant to
    /// the Boss - and that man is DERIVED from the branch he stands in, never stored on
    /// him. The number did not change; what it points at did, and so did what moves it.
    ///
    /// Nothing here writes the field. Every movement goes through
    /// <see cref="RosterOps.NudgePersonality"/> and carries a reason, because a man
    /// whose loyalty fell for a reason nobody was told is a betrayal the player could
    /// not have seen coming.
    ///
    /// Pure, free of UnityEngine and of the Outfit layer - the pay gap is handed in.
    /// </summary>
    public static class Loyalty
    {
        /// <summary>Where a new relationship starts. A man moved to a new lieutenant
        /// does not bring what he felt about the last one with him.</summary>
        public const int Neutral = 50;

        /// <summary>How much of the reset a disciplined man carries himself: a steady
        /// man gives a new superior more benefit of the doubt than a wild one, and
        /// this is the only thing that survives a transfer.</summary>
        public const int CarryFromDisciplinePercent = 20;

        /// <summary>Drift is settled weekly rather than daily - a man's opinion of his
        /// lieutenant does not move every midnight, and a weekly step keeps the whole
        /// thing legible in the paper.</summary>
        public const int DriftEveryDays = 7;

        /// <summary>Below this much Ambition, being passed over does not gnaw at
        /// him.</summary>
        public const int AmbitionFloor = 60;

        /// <summary>Days in the same rank before an ambitious man starts to feel
        /// parked. Two months of being exactly what he was.</summary>
        public const int ParkedDays = 56;

        /// <summary>What being parked costs a week.</summary>
        public const int ParkedLoss = 2;

        /// <summary>What being one of the men a lieutenant cannot actually hold costs
        /// a week (RANK-001's over-capacity, felt by the men rather than by the
        /// number).</summary>
        public const int CrowdedLoss = 1;

        /// <summary>What being paid the rate, week after week, is worth. Small: money
        /// does not buy loyalty, it only stops the bleeding.</summary>
        public const int PaidOnTimeGain = 1;

        /// <summary>Crossing DOWN through this is news. A man under it is a man the
        /// player ought to be looking at.</summary>
        public const int WatchBand = 40;

        /// <summary>
        /// He answers to somebody new. Loyalty resets toward neutral, carrying only a
        /// fraction of what he had - and the carry is his own discipline, not his old
        /// opinion. This is what stops loyalty earned under one lieutenant from being
        /// spent under another.
        /// </summary>
        public static void Reaim(Character man, string reason,
            List<PersonalityChange> into = null)
        {
            if (man == null || man.Gone)
                return;

            var steadiness = Personality.Get(man, PersonalityTrait.Discipline);
            var target = Neutral + (steadiness - Neutral) * CarryFromDisciplinePercent / 100;
            RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty,
                target - man.Loyalty, reason ?? "answers to somebody new", into);
        }

        /// <summary>
        /// One week's drift, for one man. Everything that moves it is named and
        /// printed; nothing here is a hidden percentage.
        ///
        /// superiorIsOverCapacity comes from RANK-001 - a lieutenant holding more men
        /// than he can lead is a lieutenant whose men feel it.
        /// </summary>
        public static void Drift(Character man, bool hasSuperior,
            bool superiorIsOverCapacity, int payGap, int day, int timeInRank,
            List<PersonalityChange> changes, List<Incident> incidents)
        {
            if (man == null || man.Gone || !hasSuperior)
                return;
            if (day <= 0 || day % DriftEveryDays != 0)
                return;

            var before = man.Loyalty;

            if (Personality.Get(man, PersonalityTrait.Ambition) > AmbitionFloor &&
                timeInRank > ParkedDays)
                RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, -ParkedLoss,
                    "has been exactly what he is for too long", changes);

            if (superiorIsOverCapacity)
                RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, -CrowdedLoss,
                    "one of more men than his lieutenant can lead", changes);

            if (payGap <= 0)
                RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, PaidOnTimeGain,
                    "paid the rate, week after week", changes);

            // Crossing DOWN through the watch band is the one swing worth a line in
            // the paper: it is the last warning before LOY-002's arithmetic starts
            // looking at him.
            if (before >= WatchBand && man.Loyalty < WatchBand)
                incidents?.Add(new Incident(man.Id, man.FullName,
                    IncidentKind.BearsWatching, day, "", 0,
                    IncidentText.Line(IncidentKind.BearsWatching, man.FullName, "")));
        }
    }
}
