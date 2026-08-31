namespace LivingCity.Personnel
{
    /// <summary>
    /// What a man is like, as opposed to what he is good at. Six of them, and Loyalty
    /// is one - it was here first and there is exactly one of it.
    /// </summary>
    public enum PersonalityTrait
    {
        Courage,
        Greed,
        Ambition,
        Discipline,
        Temper,
        Loyalty,
    }

    /// <summary>One trait moved, and why. The record is the point: nothing about a man
    /// may change without a line somebody can read, which is what the incident feed and
    /// the loyalty layer both hang off.</summary>
    public readonly struct PersonalityChange
    {
        public readonly int CharacterId;
        public readonly string Name;
        public readonly PersonalityTrait Trait;
        public readonly int From;
        public readonly int To;

        /// <summary>In the clerk's own words - "paid late three weeks running", "his
        /// lieutenant was shot in front of him". Printed, never parsed.</summary>
        public readonly string Reason;

        public PersonalityChange(int characterId, string name, PersonalityTrait trait,
            int from, int to, string reason)
        {
            CharacterId = characterId;
            Name = name;
            Trait = trait;
            From = from;
            To = to;
            Reason = reason;
        }

        public int Delta => To - From;
    }

    /// <summary>
    /// Personality is not a skill. It is not practised, it has no ceiling, and no
    /// amount of work moves it: it is rolled when a man is dealt and afterwards only
    /// drifts, or is knocked by something that happened to him.
    ///
    /// Kept on the 0-100 scale rather than in stars for the reason Loyalty always was:
    /// these numbers move by single points, and a five-step scale would make every
    /// nudge either invisible or enormous.
    ///
    /// The player is never shown the numbers - the personal file prints WORDS. He
    /// learns what his men are like from what they do, which is the whole design.
    /// </summary>
    public static class Personality
    {
        /// <summary>The band most men roll in.</summary>
        public const int MinRoll = 20;

        public const int MaxRoll = 80;

        /// <summary>Percent of rolls that ignore the band entirely and take the whole
        /// scale. Personality is where men differ most, and an outfit with nobody in it
        /// who is either a coward or a maniac is an outfit with no stories in it.</summary>
        public const int ExtremePercent = 8;

        public static readonly PersonalityTrait[] All =
        {
            PersonalityTrait.Courage,
            PersonalityTrait.Greed,
            PersonalityTrait.Ambition,
            PersonalityTrait.Discipline,
            PersonalityTrait.Temper,
            PersonalityTrait.Loyalty,
        };

        /// <summary>
        /// Rolls a man's character off his own stream - no draw taken from whatever
        /// sequence the caller is walking, the same discipline the hidden ceilings and
        /// the dates of birth keep.
        ///
        /// Loyalty is NOT rolled here. It was dealt before this class existed, by the
        /// seeder, in its own narrower band, and re-rolling it here would re-deal every
        /// campaign's starting six for nothing.
        /// </summary>
        public static void Roll(Character man, int stream)
        {
            if (man == null)
                return;

            for (var i = 0; i < All.Length; i++)
            {
                var trait = All[i];
                if (trait == PersonalityTrait.Loyalty)
                    continue;

                var rng = new System.Random(Potential.Mix(stream, 3_100 + (int)trait));
                var value = rng.Next(100) < ExtremePercent
                    ? rng.Next(0, 101)
                    : rng.Next(MinRoll, MaxRoll + 1);
                Set(man, trait, value);
            }
        }

        public static int Get(Character man, PersonalityTrait trait)
        {
            if (man == null)
                return 0;
            switch (trait)
            {
                case PersonalityTrait.Courage: return man.Courage;
                case PersonalityTrait.Greed: return man.Greed;
                case PersonalityTrait.Ambition: return man.Ambition;
                case PersonalityTrait.Discipline: return man.Discipline;
                case PersonalityTrait.Temper: return man.Temper;
                default: return man.Loyalty;
            }
        }

        /// <summary>Writes a trait, clamped to the scale. Only the seeder's roll and
        /// <see cref="RosterOps.NudgePersonality"/> call this - everything else goes
        /// through the nudge, so every movement carries a reason.</summary>
        public static void Set(Character man, PersonalityTrait trait, int value)
        {
            if (man == null)
                return;
            var clamped = value < 0 ? 0 : value > 100 ? 100 : value;
            switch (trait)
            {
                case PersonalityTrait.Courage: man.Courage = clamped; break;
                case PersonalityTrait.Greed: man.Greed = clamped; break;
                case PersonalityTrait.Ambition: man.Ambition = clamped; break;
                case PersonalityTrait.Discipline: man.Discipline = clamped; break;
                case PersonalityTrait.Temper: man.Temper = clamped; break;
                default: man.Loyalty = clamped; break;
            }
        }

        /// <summary>
        /// The word the personal file prints instead of the number. Five bands, and the
        /// middle one is deliberately unremarkable: a man the book has nothing to say
        /// about is a man who has never given the outfit any trouble.
        /// </summary>
        public static string Band(PersonalityTrait trait, int value)
        {
            var band = value <= 20 ? 0 : value <= 40 ? 1 : value <= 60 ? 2
                     : value <= 80 ? 3 : 4;

            switch (trait)
            {
                case PersonalityTrait.Courage:
                    return band switch
                    {
                        0 => "yellow", 1 => "cautious", 2 => "steady",
                        3 => "game", _ => "fearless",
                    };
                case PersonalityTrait.Greed:
                    return band switch
                    {
                        0 => "content", 1 => "modest", 2 => "comfortable",
                        3 => "greedy", _ => "grasping",
                    };
                case PersonalityTrait.Ambition:
                    return band switch
                    {
                        0 => "settled", 1 => "unhurried", 2 => "steady",
                        3 => "ambitious", _ => "hungry",
                    };
                case PersonalityTrait.Discipline:
                    return band switch
                    {
                        0 => "wild", 1 => "loose", 2 => "reliable",
                        3 => "disciplined", _ => "exact",
                    };
                case PersonalityTrait.Temper:
                    return band switch
                    {
                        0 => "placid", 1 => "even", 2 => "quick",
                        3 => "hot-headed", _ => "vicious",
                    };
                default:
                    return band switch
                    {
                        0 => "treacherous", 1 => "unreliable", 2 => "steady",
                        3 => "loyal", _ => "devoted",
                    };
            }
        }

        public static string Label(PersonalityTrait trait) => trait switch
        {
            PersonalityTrait.Courage => "Courage",
            PersonalityTrait.Greed => "Greed",
            PersonalityTrait.Ambition => "Ambition",
            PersonalityTrait.Discipline => "Discipline",
            PersonalityTrait.Temper => "Temper",
            PersonalityTrait.Loyalty => "Loyalty",
            _ => "",
        };
    }
}
