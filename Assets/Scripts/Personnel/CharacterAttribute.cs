namespace LivingCity.Personnel
{
    /// <summary>
    /// The eleven trades of the outfit, 1-5 stars with half-star steps. Enum order IS the
    /// ledger's display order, so reordering here reorders every character card.
    /// </summary>
    public enum CharacterAttribute
    {
        Intelligence,
        Organization,
        Business,
        Firearms,
        Fists,
        Knives,
        Arson,
        Explosives,
        Intimidation,
        Driving,
        Stealth,
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
