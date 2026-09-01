namespace LivingCity.Personnel
{
    /// <summary>How a lieutenant runs his crew's rounds (ECON-005). Set by the player,
    /// per crew; the collection tables read its integer value.</summary>
    public enum CrewPolicy
    {
        Lenient = 0,
        Normal = 1,
        Strict = 2,
        Brutal = 3,
    }

    /// <summary>What kind of a man a lieutenant is (ECON-005). DERIVED, never assigned:
    /// it is a reading of his own attributes, so it changes when he changes, and the
    /// ledger prints it as a word rather than a stat block.</summary>
    public enum LieutenantArchetype
    {
        Earner = 0,
        Negotiator = 1,
        Enforcer = 2,
        Psychopath = 3,
        Administrator = 4,
        Soldier = 5,
    }

    /// <summary>
    /// The one derivation. Pure and total: the same man reads the same word every time,
    /// and every man reads SOMETHING. Order matters - the sharpest signal wins - and
    /// the thresholds are in half-steps (7 = three and a half stars) and personality
    /// points (0-100).
    /// </summary>
    public static class LieutenantArchetypes
    {
        public static LieutenantArchetype Of(Character man)
        {
            if (man == null)
                return LieutenantArchetype.Soldier;
            return Of(
                man.GetHalfSteps(CharacterAttribute.Combat),
                man.GetHalfSteps(CharacterAttribute.Streetwise),
                man.GetHalfSteps(CharacterAttribute.Organization),
                man.GetHalfSteps(CharacterAttribute.Persuasion),
                man.GetHalfSteps(CharacterAttribute.Intimidation),
                man.Temper,
                man.Discipline);
        }

        public static LieutenantArchetype Of(
            int combat, int streetwise, int organization, int persuasion,
            int intimidation, int temper, int discipline)
        {
            // The dangerous reading first: a man whose temper outruns his discipline
            // and who can hurt people IS that before he is anything else.
            if (temper >= 70 && discipline <= 45 && combat >= 6)
                return LieutenantArchetype.Psychopath;
            if (combat >= 7 && intimidation >= 6)
                return LieutenantArchetype.Enforcer;
            if (persuasion >= 7 && persuasion >= intimidation)
                return LieutenantArchetype.Negotiator;
            if (streetwise >= 7 && organization >= 6)
                return LieutenantArchetype.Earner;
            if (organization >= 7 && combat <= 5)
                return LieutenantArchetype.Administrator;
            return LieutenantArchetype.Soldier;
        }

        /// <summary>The word the ledger prints.</summary>
        public static string Word(LieutenantArchetype archetype)
        {
            switch (archetype)
            {
                case LieutenantArchetype.Earner: return "EARNER";
                case LieutenantArchetype.Negotiator: return "NEGOTIATOR";
                case LieutenantArchetype.Enforcer: return "ENFORCER";
                case LieutenantArchetype.Psychopath: return "PSYCHOPATH";
                case LieutenantArchetype.Administrator: return "ADMINISTRATOR";
                default: return "SOLDIER";
            }
        }

        public static string Word(CrewPolicy policy)
        {
            switch (policy)
            {
                case CrewPolicy.Lenient: return "LENIENT";
                case CrewPolicy.Strict: return "STRICT";
                case CrewPolicy.Brutal: return "BRUTAL";
                default: return "NORMAL";
            }
        }
    }
}
