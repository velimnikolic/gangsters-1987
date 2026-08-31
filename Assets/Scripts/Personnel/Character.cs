using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>Hood is the trade; Lieutenant leads a branch; Boss is its roster root.</summary>
    public enum Rank
    {
        Hood,
        Lieutenant,
        // Appended so existing serialized Hood/Lieutenant values keep their meaning.
        Boss,
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

        /// <summary>What he does when it turns dangerous.</summary>
        public int Courage = 50;

        /// <summary>What he does when he is underpaid and the money is right there.</summary>
        public int Greed = 50;

        /// <summary>How long he will stand being what he currently is.</summary>
        public int Ambition = 50;

        /// <summary>Whether the job happens the way it was ordered.</summary>
        public int Discipline = 50;

        /// <summary>Whether a job that needed no shooting ends in shooting anyway.</summary>
        public int Temper = 50;

        /// <summary>Flagged men are shot on sight by unbribed police - the ledger only
        /// displays it; the police layer will own setting it.</summary>
        public bool Wanted;

        /// <summary>The year he was born, on the campaign's calendar. A year rather
        /// than an age for the same reason <see cref="BackOnDay"/> is a day rather than
        /// a countdown: a stored age would have to be ticked, and anything that is
        /// ticked drifts across a long soak or a save. Zero on a man dealt without a
        /// date, who then never ages.</summary>
        public int BirthYear;

        /// <summary>Which day of the campaign year his birthday falls on, 0-based -
        /// the one day the aging pass looks at him.</summary>
        public int BirthDayOfYear;

        /// <summary>What he signed for, a week - 0 when he is paid the house scale.
        /// Only a man taken on out of the newspaper's classified column carries one
        /// (see Outfit.HireMarket): he named his price in print, and the outfit agreed
        /// to it. Kept on the man rather than derived from his stats because it is a
        /// BARGAIN, not a rate - training him up must not quietly raise it.</summary>
        public int WageAsked;

        /// <summary>The campaign day the outfit started paying him under the rate for
        /// a man of his stats; 0 when it is not. A DAY rather than a count of them,
        /// for the reason every other clock in this class is: a counter drifts across
        /// a long soak and a stored day cannot.</summary>
        public int UnderpaidSince;

        /// <summary>He is taking a cut of whatever he handles. Nothing tells the player
        /// this - it shows as thin takes on a block until somebody catches him.</summary>
        public bool Skimming;

        /// <summary>What he has asked to be paid, a day; 0 when he has asked for
        /// nothing. The player answers it - granting it moves his bargain, refusing it
        /// costs loyalty.</summary>
        public int WageDemand;

        /// <summary>The campaign day he is back on his feet. Meaningful only while he
        /// is Jailed or Hospitalized; the day tick reads it and puts him back to work.
        /// A day rather than a countdown so the figure survives a save and cannot drift
        /// with however many ticks happened to run.</summary>
        public int BackOnDay;

        /// <summary>
        /// What is actually wrong with him, in the clerk's own words - "two ribs and a
        /// wrist", "held at Rikers". Free text on purpose: the roster's CONDITION column
        /// prints the STATUS as a state word and this underneath it, and no enum will
        /// ever cover what happens to a man on a bad night. Written when he goes down
        /// (RosterOps.Hospitalize / Jail) and cleared when he stands up, so a man on his
        /// feet never carries an old note.
        /// </summary>
        public string ConditionNote = "";

        /// <summary>His record with the city, oldest line first. Dealt with him (every
        /// man on this payroll has a past) and added to as the outfit earns him more.
        /// See <see cref="RapSheet"/>; the personal file prints it.</summary>
        public readonly List<RapEntry> RapSheet = new List<RapEntry>();

        readonly int[] halfSteps = new int[AttributeScale.Count];

        /// <summary>
        /// A man starts on the scale's own floor - one star at everything - rather than
        /// on the zero a bare array gives him. "Nobody in this line of work is a zero"
        /// is <see cref="AttributeScale.MinHalfSteps"/>'s whole reason for existing, and
        /// leaving the field at 0 made that true only for men somebody remembered to
        /// deal stats to: a hand-built character sat under the floor, and the growth
        /// curve - which prices a step from where he stands - would never move him off
        /// it, because there is no step from below the bottom.
        /// </summary>
        public Character()
        {
            for (var i = 0; i < halfSteps.Length; i++)
                halfSteps[i] = AttributeScale.MinHalfSteps;
        }

        /// <summary>Points banked toward the NEXT half-step of each attribute - what a
        /// man has learned on the job and not yet grown into. Parallel to
        /// <see cref="halfSteps"/> and never converted here: accumulation and promotion
        /// are separate on purpose, so a stat can only ever rise at the day tick
        /// (Practice.Convert) and never in the middle of the job that earned it.</summary>
        readonly int[] practice = new int[AttributeScale.Count];

        /// <summary>How far he could ever get at each trade, on the 0-100 convention -
        /// rolled once when he is dealt (<see cref="Potential"/>) and never shown. Zero
        /// means UNSET, which reads as no ceiling at all: a character built by hand in
        /// a test or a fixture is not silently pinned to one star.
        ///
        /// Private with no property over it on purpose. The growth code asks
        /// <see cref="PotentialHalfSteps"/> for one number at a time; nothing that
        /// paints a page is given the array to iterate, so no ledger row can ever
        /// grow a ceiling column by accident.</summary>
        readonly int[] potential = new int[AttributeScale.Count];

        public string FullName => FirstName + " " + Surname;

        public int GetHalfSteps(CharacterAttribute attribute) => halfSteps[(int)attribute];

        public int GetPractice(CharacterAttribute attribute) => practice[(int)attribute];

        /// <summary>His ceiling at this trade in half-steps - what the growth curve
        /// prices against and stops at. Five stars when the ceiling was never rolled.</summary>
        public int PotentialHalfSteps(CharacterAttribute attribute)
        {
            var value = potential[(int)attribute];
            return value <= 0
                ? AttributeScale.MaxHalfSteps
                : AttributeScale.HalfStepsFor(value);
        }

        /// <summary>The raw 0-100 ceiling, for the balance dumps that tune the roll.
        /// Nothing the player can see may call this.</summary>
        public int PotentialValue(CharacterAttribute attribute) => potential[(int)attribute];

        /// <summary>Sets the ceiling. Only <see cref="Potential"/> and the doors that
        /// deal a man call this; a ceiling is rolled once and never edited afterwards.</summary>
        public void SetPotential(CharacterAttribute attribute, int value) =>
            potential[(int)attribute] = value < 0 ? 0 : value;

        /// <summary>Banks work done. Negative points are ignored rather than clamped
        /// away silently - nothing in the design takes practice back.</summary>
        public void AddPractice(CharacterAttribute attribute, int points)
        {
            if (points <= 0)
                return;
            practice[(int)attribute] += points;
        }

        /// <summary>Spends banked practice. Only <see cref="Practice"/> calls this.</summary>
        public void SpendPractice(CharacterAttribute attribute, int points)
        {
            var left = practice[(int)attribute] - points;
            practice[(int)attribute] = left > 0 ? left : 0;
        }

        /// <summary>Sum across all eleven attributes - the wage table's talent measure.</summary>
        public int TotalHalfSteps()
        {
            var total = 0;
            for (var i = 0; i < halfSteps.Length; i++)
                total += halfSteps[i];
            return total;
        }

        /// <summary>Sets a stat, never above the man's ceiling. The cap binds HERE
        /// rather than at each caller so no door - the seeder, the classified column,
        /// a future event - can deal a man a star he was never capable of.</summary>
        public void SetHalfSteps(CharacterAttribute attribute, int value)
        {
            var capped = AttributeScale.Clamp(value);
            var ceiling = PotentialHalfSteps(attribute);
            halfSteps[(int)attribute] = capped > ceiling ? ceiling : capped;
        }
    }
}
