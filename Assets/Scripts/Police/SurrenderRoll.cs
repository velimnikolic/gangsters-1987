namespace LivingCity.Police
{
    /// <summary>
    /// HANDS UP, OR NOT. What a crew does when an officer walks up with his piece out
    /// and puts the question - decided by the men themselves rather than by two keys on
    /// the player's keyboard.
    ///
    /// The old arrest asked the PLAYER (Y/N, and silence was a refusal). That made an
    /// arrest a menu, and it made every lieutenant in the city the same man: a coward
    /// and a hothead answered identically because the answer was never theirs. So the
    /// answer is rolled off who is actually stood there - the commanding lieutenant's
    /// nerve first, then the temper of the men behind him, and against both of those
    /// what they think of the outfit that would have to get them out.
    ///
    /// The player is not shut out: an explicit attack order on the law while the
    /// question stands overrules the roll (see PoliceDispatch.Arrest). What he cannot
    /// do any more is answer for men who would never have answered that way.
    ///
    /// Pure and free of UnityEngine like the rest of the sim's arithmetic, so the
    /// headless suite can put the question a thousand times without an editor.
    /// </summary>
    public static class SurrenderRoll
    {
        // THE WEIGHTS, IN ONE PLACE. A brave lieutenant fights; hot men fight; men who
        // trust the outfit to get them out go quietly, which is why loyalty SUBTRACTS.
        // Tuned so a middling crew (50/50/50) stands at 0.30 - the ordinary crew goes
        // quietly two times in three - and the extremes reach both ends of the band.
        public const float CourageWeight = 0.5f;
        public const float TemperWeight = 0.3f;
        public const float LoyaltyWeight = -0.2f;

        /// <summary>Nobody is certain. Even the meekest crew has a man who bolts, and
        /// the hardest lieutenant in the city has had a night where he put his hands
        /// up - a 0 or a 1 here would make the whole roll a lookup table.</summary>
        public const float Floor = 0.05f;
        public const float Ceiling = 0.95f;

        /// <summary>Where a crew with nobody on the books stands - a rival mob, whose
        /// men the outfit's ledger has never heard of. The middle of the band, so the
        /// street still shows both answers.</summary>
        public const int NoBooks = 50;

        /// <summary>
        /// The odds this crew fights rather than goes quietly, 0-1.
        /// </summary>
        /// <param name="lieutenantCourage">The commanding man's nerve, 0-100. When he is
        /// not on the street it is the senior man present who answers for the crew.</param>
        /// <param name="averageTemper">Mean temper of the men stood there, 0-100.</param>
        /// <param name="averageLoyalty">Mean loyalty of the men stood there, 0-100.</param>
        public static float FightChance(int lieutenantCourage, int averageTemper, int averageLoyalty)
        {
            var raw = CourageWeight * Clamp01(lieutenantCourage / 100f) +
                      TemperWeight * Clamp01(averageTemper / 100f) +
                      LoyaltyWeight * Clamp01(averageLoyalty / 100f);
            return raw < Floor ? Floor : raw > Ceiling ? Ceiling : raw;
        }

        /// <summary>Puts the question. Deterministic: the same stream and the same odds
        /// always answer the same, so a seeded run can be replayed.</summary>
        public static bool Fights(float chance, int stream) =>
            new System.Random(stream).NextDouble() < chance;

        /// <summary>The stream one crew's answer at one incident is rolled off. Its own
        /// mix per (crew, incident) so that a crew asked twice over two different
        /// shootings is not asked the same question twice.</summary>
        public static int StreamFor(int citySeed, int crewKey, int incident) =>
            Personnel.Potential.Mix(
                citySeed + Generation.SeedOffsets.Police + 700,
                unchecked(crewKey * 31 + incident));

        /// <summary>How it reads over the street while the question stands. The player
        /// is told the LEANING and never the number: he is watching men, not a dice
        /// roll, and the banner is there to tell him whether to intervene.</summary>
        public static string Leaning(float chance) =>
            chance >= 0.60f ? "itching to fight"
            : chance >= 0.38f ? "wavering"
            : "going quietly";

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
