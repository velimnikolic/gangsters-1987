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
    /// The banking is deliberately CAPPED PER DAY. A firefight is one lesson, however
    /// many rounds it takes: without the cap a crew pinned down for a minute of real
    /// time would come home better shots than a crew that spent a fortnight working,
    /// and the improvement system would reward standing in the open.
    /// </summary>
    public static class CrewSkill
    {
        /// <summary>Practice points one hit is worth to the man who landed it.</summary>
        public const int PointsPerHit = 1;

        /// <summary>The most a man can learn from shooting in one day.</summary>
        public const int DailyCap = 5;

        /// <summary>What the gun's own accuracy is multiplied by: 0.82 at one star,
        /// 1.30 at five.</summary>
        public static float Aim(int halfSteps) =>
            0.70f + 0.06f * AttributeScale.Clamp(halfSteps);

        /// <summary>
        /// A round that found its mark taught him something. Ignores rivals, who carry
        /// negative ids and are on nobody's books, and men whose crews are dealt in a
        /// scene with no ledger behind them.
        /// </summary>
        public static void Landed(int characterId, CharacterAttribute attribute)
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
            if (!Allow(characterId, day))
                return;

            member.AddPractice(attribute, PointsPerHit);
        }

        // Whose lessons have been counted, and for which day. Cleared wholesale the
        // moment the day changes rather than per entry: one comparison, and the table
        // can never carry a stale count into tomorrow.
        static readonly System.Collections.Generic.Dictionary<int, int> Counted =
            new System.Collections.Generic.Dictionary<int, int>();
        static int countedDay = -1;

        static bool Allow(int characterId, int day)
        {
            if (day != countedDay)
            {
                Counted.Clear();
                countedDay = day;
            }

            Counted.TryGetValue(characterId, out var today);
            if (today >= DailyCap)
                return false;
            Counted[characterId] = today + 1;
            return true;
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
