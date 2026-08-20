namespace LivingCity.Personnel
{
    /// <summary>Hood is the trade; Lieutenant leads a crew and answers to the boss.</summary>
    public enum Rank
    {
        Hood,
        Lieutenant,
    }

    /// <summary>
    /// A specialist is hired, never recruited off a corner - the accountant and the lawyer
    /// come through their own doors later. Anything other than None means the character can
    /// never be crewed, promoted, or put on the front.
    /// </summary>
    public enum Specialty
    {
        None,
        Accountant,
        Lawyer,
    }

    public enum CharacterStatus
    {
        Active,
        Jailed,
        Hospitalized,
        Dead,
        /// <summary>Ran from a fight and never came back: struck off like the dead - his
        /// line kept, his gear pooled, his post filled - but with no grave.</summary>
        Deserted,
    }

    /// <summary>
    /// One person on the books. Pure data, free of UnityEngine, so the headless suite can
    /// build and mutate rosters without an Editor. Everything cross-references by
    /// <see cref="Id"/> - a Character never holds another Character, which is what lets a
    /// future save system serialize the roster as three flat lists.
    ///
    /// Deliberately absent, on purpose: no crew field (the Crew owns its member ids; a
    /// character's assignment is DERIVED - see Roster.AssignmentOf), no equipment list
    /// (RosterEquipment holds the holder id - the item is the single source of who has it),
    /// and no order/time-budget fields yet - the weekly command layer will attach those to
    /// the crew, keyed by these ids, without touching this class.
    /// </summary>
    public sealed class Character
    {
        public int Id;
        public string FirstName = "";
        public string Surname = "";
        public Rank Rank = Rank.Hood;
        public Specialty Specialty = Specialty.None;
        public CharacterStatus Status = CharacterStatus.Active;

        /// <summary>The body he wears - in his ledger photograph and on the street both.
        /// Kept HERE, with the man, rather than worked out from his rank each time it is
        /// asked for: a promotion is a change of rank, not of face. Made a lieutenant, a
        /// hood used to walk out of the room a different man in a different coat, with
        /// the street swapping his body under him mid-stride. Empty until he is first
        /// cast (GangLooks.LookFor), and only changed when a crewmate already wears it -
        /// no two men in one crew are the same man.</summary>
        public string Look = "";

        /// <summary>Off the books for good - dead or deserted: struck through, unpaid,
        /// beyond promotion or a gun.</summary>
        public bool Gone => Status == CharacterStatus.Dead || Status == CharacterStatus.Deserted;

        /// <summary>0-100 rather than stars: weekly drift will nudge this by single points,
        /// and a five-step scale would make every nudge invisible or enormous.</summary>
        public int Loyalty = 50;

        /// <summary>Flagged men are shot on sight by unbribed police - the ledger only
        /// displays it; the police layer will own setting it.</summary>
        public bool Wanted;

        readonly int[] halfSteps = new int[AttributeScale.Count];

        public string FullName => FirstName + " " + Surname;

        public int GetHalfSteps(CharacterAttribute attribute) => halfSteps[(int)attribute];

        /// <summary>Sum across all eleven attributes - the wage table's talent measure.</summary>
        public int TotalHalfSteps()
        {
            var total = 0;
            for (var i = 0; i < halfSteps.Length; i++)
                total += halfSteps[i];
            return total;
        }

        public void SetHalfSteps(CharacterAttribute attribute, int value) =>
            halfSteps[(int)attribute] = AttributeScale.Clamp(value);
    }
}
