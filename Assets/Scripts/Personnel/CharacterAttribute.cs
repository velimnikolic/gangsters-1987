namespace LivingCity.Personnel
{
    /// <summary>
    /// The eleven general skills a man is judged on, 1-5 stars with half-star steps.
    /// Enum order IS the ledger's display order, so reordering here reorders every
    /// character card: the field trades first, the command trades next, the social
    /// trades last, which is the order the personal file reads top to bottom.
    ///
    /// General, not specialist: melee, gun work and blade work are one COMBAT number
    /// because the game never asks which of the three a man used, and a torch job or a
    /// charge reads the same hand. A specialist skill is only worth a slot when some
    /// formula makes it the dominant term - anything that cannot name such a site does
    /// not belong on this list.
    /// </summary>
    public enum CharacterAttribute
    {
        /// <summary>Every violent trade in one number: guns, fists, blades, fire and
        /// powder - and the physical resilience to still be standing afterwards.</summary>
        Combat,

        /// <summary>What he notices: a tail, a wire, a set of books that do not add
        /// up, a corner boy worth bringing in.</summary>
        Awareness,

        /// <summary>Moving unseen - scouting another outfit's turf, and leaving a job
        /// without the street being able to say who was on it.</summary>
        Stealth,

        /// <summary>Behind the wheel.</summary>
        Driving,

        /// <summary>How the street works: what a shop really turns over, what a
        /// licence costs, where a premises is worth taking.</summary>
        Streetwise,

        /// <summary>Command of men - how many will follow him, and how well they hold
        /// when it goes wrong.</summary>
        Leadership,

        /// <summary>Running the machine: rotas, gear, and how much of what he was
        /// given actually reaches his crew's hands.</summary>
        Organization,

        /// <summary>What the street concedes him on sight - the standing a made name
        /// carries into a room before he opens his mouth.</summary>
        StreetAuthority,

        /// <summary>Talking a man round - the deal that is taken rather than
        /// forced.</summary>
        Persuasion,

        /// <summary>The lean: the same deal, taken because of what happens if it is
        /// not.</summary>
        Intimidation,

        /// <summary>Who he knows outside the outfit - police, lawyers, judges,
        /// suppliers, and whoever owes him a call.</summary>
        Connections,
    }

    /// <summary>
    /// The star scale, stored as integer HALF-STEPS: 2 = one star, 7 = three and a half,
    /// 10 = five. Integers rather than floats because sorting, equality and the headless
    /// assertions must be exact - and because the future weekly-improvement system bumps a
    /// stat by "+1 half-step" with no rounding policy to argue about. Floats exist only at
    /// display time, via <see cref="Stars"/>.
    /// </summary>
    public static class AttributeScale
    {
        public const int Count = 11;

        /// <summary>One full star - nobody in this line of work is a zero.</summary>
        public const int MinHalfSteps = 2;

        /// <summary>Five full stars.</summary>
        public const int MaxHalfSteps = 10;

        public static float Stars(int halfSteps) => Clamp(halfSteps) * 0.5f;

        public static int Clamp(int halfSteps)
        {
            if (halfSteps < MinHalfSteps)
                return MinHalfSteps;
            return halfSteps > MaxHalfSteps ? MaxHalfSteps : halfSteps;
        }
    }
}
