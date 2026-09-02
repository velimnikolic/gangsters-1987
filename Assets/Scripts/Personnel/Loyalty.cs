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

        /// <summary>
        /// Crossing DOWN through this is news. A man under it is a man the player
        /// ought to be looking at.
        ///
        /// THE number for "we are losing him", and the only one: the ledger's red flag
        /// reads it (<see cref="ManFlags.LoyaltyForRedFlag"/>) and the personal file
        /// prints his loyalty in red beneath it. Three pages once carried three
        /// different figures, so a man at 38 was named in the paper and printed in
        /// black ink on his own file.
        /// </summary>
        public const int WatchBand = 35;

        /// <summary>What being taken back down costs a man on top of the reset, before
        /// his own ambition is counted. A promotion he was given can be taken away; the
        /// insult cannot.</summary>
        public const int TakenDownSting = 15;

        /// <summary>How much of his Ambition is added to the sting. A settled man
        /// shrugs and goes back to a corner; a hungry one starts counting what he is
        /// owed - at Ambition 100 that is another twenty points off.</summary>
        public const int StingFromAmbitionPercent = 20;

        // ---------------------------------------------------------------- GAN-245

        /// <summary>
        /// WHAT SELLING A MAN COSTS. The boss had a lieutenant inside and struck him off
        /// rather than carry him ("jer si ga prodao"), and the whole outfit hears about
        /// it - hardest in the crew that answered to him, then every other lieutenant
        /// who has just learned what he is worth, then everybody else.
        ///
        /// The crew's own cut is softened by a man's Discipline the same way a
        /// transfer's reset is (<see cref="CarryFromDisciplinePercent"/>): a steady man
        /// takes the news as news, a wild one takes it personally.
        /// </summary>
        public const int CutLooseCrewHit = 25;

        /// <summary>What every OTHER lieutenant takes off it. They are the men it could
        /// happen to next.</summary>
        public const int CutLooseLieutenantHit = 10;

        /// <summary>What the rest of the outfit takes off it.</summary>
        public const int CutLooseOutfitHit = 5;

        /// <summary>Cutting a HOOD loose is a smaller thing and is felt as one: his own
        /// crew notices, the rest of the city barely does.</summary>
        public const int CutLooseHoodCrewHit = 10;

        /// <summary>See <see cref="CutLooseHoodCrewHit"/>.</summary>
        public const int CutLooseHoodOutfitHit = 2;

        /// <summary>What standing by a man inside is worth a week, to the crew whose
        /// leader it is. One point: it is not a strategy, it is the absence of a
        /// betrayal, and it takes a month of it to undo one sale.</summary>
        public const int StoodByGain = 1;

        /// <summary>What a crew feels when its leader is inside AND his envelope was
        /// empty - on top of the ordinary unpaid hit. The one thing worse than selling
        /// a man is keeping him on the books and not paying him while he does your
        /// time.</summary>
        public const int InsideUnpaidLoss = 3;

        /// <summary>
        /// What one man actually takes off a cut-loose, given the outfit-wide figure.
        /// A steady man hears it as news about the outfit; a wild one hears it as news
        /// about himself, and the difference is his Discipline at exactly the weight a
        /// transfer's carry uses (<see cref="CarryFromDisciplinePercent"/>) - one idea,
        /// one constant.
        /// </summary>
        public static int CutLooseHit(Character man, int fullHit)
        {
            if (man == null || fullHit <= 0)
                return 0;
            var steadiness = Personality.Get(man, PersonalityTrait.Discipline);
            var softened = fullHit -
                           fullHit * steadiness * CarryFromDisciplinePercent / 10_000;
            return softened < 1 ? 1 : softened;
        }

        /// <summary>
        /// The day his rank last changed, as his file would answer it.
        ///
        /// <see cref="Character.RankSince"/> is stamped on every rank change and left
        /// at zero on a man who has never had one - so a hood who has been a hood
        /// since the day he came on would read as having been parked since the founding
        /// of the city. He has been exactly what he is since he SIGNED, and the file
        /// already knows that day (<see cref="Career.JoinedDay"/>), so it is read from
        /// there rather than stored twice.
        /// </summary>
        public static int RankSinceDay(Character man)
        {
            if (man == null)
                return 0;
            return man.RankSince > 0 ? man.RankSince : Career.JoinedDay(man);
        }

        /// <summary>How long he has been exactly what he is, in days. THE figure
        /// <see cref="Drift"/> is fed and the one the ledger prints, so the page and
        /// the arithmetic can never disagree about him.</summary>
        public static int TimeInRank(Character man, int today)
        {
            var days = today - RankSinceDay(man);
            return days > 0 ? days : 0;
        }

        /// <summary>
        /// He is ambitious enough to mind, and he has been in the same job long enough
        /// to. This is exactly the pair <see cref="Drift"/> charges <see cref="ParkedLoss"/>
        /// a week for, read out loud so a page can say WHY a good lieutenant is souring
        /// - which is a decision the player could have acted on.
        ///
        /// It never acts. Drift charges off its own arithmetic and would charge the
        /// same man the same points if nothing ever printed this.
        /// </summary>
        public static bool IsParked(Character man, int today) =>
            man != null && !man.Gone &&
            Personality.Get(man, PersonalityTrait.Ambition) > AmbitionFloor &&
            TimeInRank(man, today) > ParkedDays;

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
        /// The cut a demotion leaves after the reset. Separate from
        /// <see cref="Reaim"/> on purpose: every transfer resets a man, but only one
        /// kind of transfer is an insult, and the insult is what makes taking a crew
        /// off a man a decision rather than a menu item.
        /// </summary>
        public static void Sting(Character man, List<PersonalityChange> into = null)
        {
            if (man == null || man.Gone)
                return;

            var hunger = Personality.Get(man, PersonalityTrait.Ambition);
            var cut = TakenDownSting + hunger * StingFromAmbitionPercent / 100;
            RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, -cut,
                "had a crew, and had it taken off him", into);
        }

        /// <summary>
        /// One week's drift, for one man. Everything that moves it is named and
        /// printed; nothing here is a hidden percentage.
        ///
        /// superiorIsOverCapacity comes from RANK-001 - a lieutenant holding more men
        /// than he can lead is a lieutenant whose men feel it.
        ///
        /// leaderInside / leaderPaid are GAN-245's other side: standing by a man the
        /// city has taken is worth something to the men who watch you do it, and
        /// standing by him without paying him is worth rather more than nothing in the
        /// wrong direction.
        /// </summary>
        public static void Drift(Character man, bool hasSuperior,
            bool superiorIsOverCapacity, int payGap, int day, int timeInRank,
            List<PersonalityChange> changes, List<Incident> incidents,
            bool leaderInside = false, bool leaderPaid = true)
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

            // GAN-245: the men can see who the boss carries and who he does not.
            if (leaderInside)
            {
                if (leaderPaid)
                    RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, StoodByGain,
                        "the boss is standing by a man inside", changes);
                else
                    RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty,
                        -InsideUnpaidLoss,
                        "their man is inside and his envelope was empty", changes);
            }

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
