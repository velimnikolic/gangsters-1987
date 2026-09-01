using LivingCity.Personnel;

namespace RoadDemo
{
    /// <summary>
    /// Where the ledger's attribute sheet meets the street. Two jobs, both shared
    /// rather than scene-local on purpose (the house rule: behaviour lives in shared
    /// classes and scenes only configure them) - turn a man's half-steps into the
    /// multiplier the fight applies, and bank what he learns doing it back onto his
    /// line in the book.
    ///
    /// The banking is deliberately ONE LESSON A DAY, per man and per kind of work. A
    /// firefight is one lesson however many rounds it takes: without the cap a crew
    /// pinned down for a minute of real time would come home better shots than a crew
    /// that spent a fortnight working, and the improvement system would reward standing
    /// in the open. What the lesson is WORTH is not decided here - it comes off
    /// <see cref="ActivityXp"/> like every other point of practice in the game.
    /// </summary>
    public static class CrewSkill
    {
        /// <summary>What the gun's own accuracy is multiplied by: 0.82 at one star,
        /// 1.30 at five.</summary>
        public static float Aim(int halfSteps) =>
            0.70f + 0.06f * AttributeScale.Clamp(halfSteps);

        /// <summary>
        /// A round that found its mark taught him something - firing off a magazine
        /// into a wall teaches nobody anything, which is why this is called on the hit
        /// and not on the shot. Ignores rivals, who carry negative ids and are on
        /// nobody's books, and men whose crews are dealt in a scene with no ledger
        /// behind them.
        /// </summary>
        public static void Landed(int characterId) =>
            Learn(characterId, Activity.AttackOnARival, XpOutcome.Completed);

        /// <summary>
        /// He got them out. The man at the bars never fires - both hands stay on them -
        /// so without this the one man on a drive-by who did the hardest part of it
        /// would come home having learned nothing at all.
        /// </summary>
        public static void Drove(int characterId, bool clean) =>
            Learn(characterId, Activity.Getaway,
                clean ? XpOutcome.Completed : XpOutcome.Partial);

        /// <summary>
        /// He walked the round and stood at the door (XP-003). Ordering a shakedown on
        /// paper trains a man; sending the same man to do it on his feet has to train
        /// him too, or the book teaches what the street does not. A door that paid is a
        /// job done; a door that did not is the half-paid collection the table already
        /// has a word for.
        /// </summary>
        public static void Collected(int characterId, bool paid) =>
            Learn(characterId, Activity.RacketCollection,
                paid ? XpOutcome.Completed : XpOutcome.Partial);

        /// <summary>
        /// He leaned on somebody at his own door - asked, threatened, or swung for it
        /// (XP-003). What he learns is the same lesson the ordered shakedown banks,
        /// because it is the same work.
        /// </summary>
        public static void Leaned(int characterId, bool gaveIn) =>
            Learn(characterId, Activity.Leaning,
                gaveIn ? XpOutcome.Completed : XpOutcome.Partial);

        static void Learn(int characterId, Activity activity, XpOutcome outcome)
        {
            if (characterId < 0)
                return;

            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            var member = director != null && director.Roster != null
                ? director.Roster.Find(characterId)
                : null;
            if (member == null || member.Gone)
                return;

            var day = LivingCity.Gameplay.OutfitDirector.Instance != null
                ? LivingCity.Gameplay.OutfitDirector.Instance.Campaign.Day
                : 0;
            if (!Allow(characterId, activity, day))
                return;

            ActivityXp.Award(member, activity, outcome);
        }

        // Whose lessons have been counted, and for which day. Cleared wholesale the
        // moment the day changes rather than per entry: one comparison, and the table
        // can never carry a stale count into tomorrow. The key is the man AND the kind
        // of work, so a night that involved both a firefight and a getaway teaches him
        // both without either standing in for the other.
        static readonly System.Collections.Generic.HashSet<long> Counted =
            new System.Collections.Generic.HashSet<long>();
        static int countedDay = -1;

        static bool Allow(int characterId, Activity activity, int day)
        {
            if (day != countedDay)
            {
                Counted.Clear();
                countedDay = day;
            }

            return Counted.Add((long)characterId * 64 + (int)activity);
        }

        // Static state outlives Play when domain reload is off - the same trap
        // OverlayRegistry and DayClock reset against.
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Counted.Clear();
            countedDay = -1;
        }
    }
}
