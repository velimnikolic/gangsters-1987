namespace LivingCity.Personnel
{
    /// <summary>
    /// How far a man can ever get at a trade - the ceiling he was born with, rolled
    /// once when he is dealt and never shown to anybody. The player watches a hood
    /// stop improving and draws his own conclusion, which is the same information a
    /// visible number would carry and none of the frustration.
    ///
    /// Rolled off the man's OWN stream rather than the seeder's shared draw sequence:
    /// adding a ceiling must not re-deal a campaign's starting six, and two men next
    /// to each other in the books must not share a roll. The stream is mixed from the
    /// roster seed, his id and the skill index, so the same seed produces the same
    /// eleven ceilings for the same man however many times the roster is built.
    /// </summary>
    public static class Potential
    {
        /// <summary>Nobody is born hopeless and nobody is born finished. On the 0-100
        /// convention (value = half-steps x 10), 35 is under two stars and 95 is five:
        /// most men land in the middle and a few are worth building.</summary>
        public const int MinRoll = 35;

        public const int MaxRoll = 95;

        /// <summary>How many draws the roll keeps the lowest of. Three, because a flat
        /// draw across 35-95 would make the average hood a three-and-a-half star man at
        /// everything and there would be nobody to build. Keeping the lowest of three
        /// centres the band on 50 and puts 57 % of all ceilings in 40-60 - the spec's
        /// "most hoods, most skills" - while leaving the 90s rare enough that finding a
        /// man who can reach them is an event. The shape is the lever a balance pass
        /// turns; the band is the other one.</summary>
        public const int Draws = 3;

        /// <summary>Rolls the eleven ceilings onto a man. The band is a parameter so a
        /// future recruit-source distribution (a college boy against a corner boy) has
        /// somewhere to hang without this function being rewritten.</summary>
        public static void Roll(Character man, int stream, int min = MinRoll,
            int max = MaxRoll)
        {
            if (man == null)
                return;
            if (min > max)
                (min, max) = (max, min);

            for (var a = 0; a < AttributeScale.Count; a++)
            {
                // A fresh System.Random per skill rather than one walked eleven times:
                // the walk would make skill 3's ceiling depend on how many skills were
                // asked for before it, and the enum order is not a promise.
                var rng = new System.Random(Mix(stream, a));

                var roll = max;
                for (var d = 0; d < Draws; d++)
                {
                    var draw = rng.Next(min, max + 1);
                    if (draw < roll)
                        roll = draw;
                }
                man.SetPotential((CharacterAttribute)a, roll);
            }
        }

        /// <summary>The stream one man's ceilings are rolled off. Kept here so every
        /// door that deals a character (the seeder, the recruiter, the classified
        /// column) derives it the same way.</summary>
        public static int StreamFor(int rosterSeed, int characterId) =>
            Mix(rosterSeed + Generation.SeedOffsets.Personnel + 500, characterId);

        /// <summary>Avalanches two numbers before System.Random sees them - the same
        /// mix the newspaper's editions and the job rolls use. Nearby seeds and
        /// consecutive ids are as correlated as two numbers get.</summary>
        public static int Mix(int seed, int salt)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)salt * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (int)h;
            }
        }
    }
}
