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

    /// <summary>
    /// STANDING WORK a man is marked for, over and above the crew he is in.
    ///
    /// A duty is not a rank and not a specialty: he is an ordinary hood who has been
    /// given a standing job, and the sim reads the mark rather than being told each
    /// time. COLLECTOR is the one that exists - a man on the bag walks his lieutenant's
    /// blocks on their collection day and banks the take. The others in the design
    /// (Enforcer, Driver, Guard) are not here because nothing would read them yet.
    /// </summary>
    public enum Duty
    {
        None,
        Collector,
        // Appended so serialized None/Collector values keep their meaning.
        Escort,

        /// <summary>He keeps a flat. Unlike the bag and its escort, a keeper is OFF THE
        /// STREET: he stands in the room instead of walking with a crew, and pulling him
        /// back into one darkens the flat that moment (EPIC 27).</summary>
        Keeper,
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

        /// <summary>Another family has him. He is off the books for as long as they hold
        /// him and comes back in a bed, not on his feet (RIVAL-009).</summary>
        Taken,

        /// <summary>
        /// SOLD (GAN-245). The boss had a man inside and decided not to carry him: off
        /// the books, off the payroll, off the case. Appended so every serialized
        /// Active/Jailed/Hospitalized/Dead/Deserted keeps its meaning.
        ///
        /// Struck off like a deserter, and counted as <see cref="Character.Gone"/> for
        /// exactly the same reasons - but it is the OUTFIT that did it, and the rest of
        /// the men are told so (Loyalty.CutLoose*).
        /// </summary>
        CutLoose,
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

        /// <summary>The standing job he is marked for, if any. Set through
        /// RosterOps.SetDuty, which is where the rules about who may carry one live;
        /// every move that changes who he answers to clears it.</summary>
        public Duty Duty = Duty.None;

        public CharacterStatus Status = CharacterStatus.Active;

        /// <summary>The body he wears - in his ledger photograph and on the street both.
        /// Kept HERE, with the man, rather than worked out from his rank each time it is
        /// asked for: a promotion is a change of rank, not of face. Made a lieutenant, a
        /// hood used to walk out of the room a different man in a different coat, with
        /// the street swapping his body under him mid-stride. Empty until he is first
        /// cast (GangLooks.LookFor), and only changed when a crewmate already wears it -
        /// no two men in one crew are the same man.</summary>
        public string Look = "";

        /// <summary>The voice he speaks in - a bank id off the recorded sheet ("VB03"),
        /// settled the first time he says anything (VoiceCasting.BankFor) and kept on him
        /// from then on, exactly like <see cref="Look"/>. Stored rather than worked out
        /// from his id each time for the reason the coat is: a ninth actor added to the
        /// folder later would otherwise re-cast every man in every save, and a man whose
        /// voice changes between two sessions is a stranger giving the orders.</summary>
        public string Voice = "";

        /// <summary>Off the books for good - dead, deserted, or cut loose by the boss:
        /// struck through, unpaid, beyond promotion or a gun.</summary>
        public bool Gone => Status == CharacterStatus.Dead ||
                            Status == CharacterStatus.Deserted ||
                            Status == CharacterStatus.CutLoose;

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

        /// <summary>
        /// HOW BADLY THE CITY WANTS HIM, 0-3 (GAN-222).
        ///
        /// The old doc comment on this field said "shot on sight by unbribed police",
        /// which was never true and is not the design: the police CHASE a wanted man and
        /// try to take him, and they shoot only if he fights. Three grades, and the
        /// grade is a fact about what he did:
        ///
        ///   1 - he ran rather than be arrested,
        ///   2 - he came out of the back of a wrecked prisoner transfer,
        ///   3 - he killed a policeman, and THAT one never cools.
        ///
        /// Cooling is hidden time and nothing else: no disguise, no bribe, no lawyer.
        /// See <see cref="HidingSince"/> and LivingCity.Police.WantedLevels.
        /// </summary>
        public int WantedLevel;

        /// <summary>Wanted at all. Kept as a property so every older read of the flag
        /// survives the change to a grade; setting it true grades a clean man at 1 and
        /// never demotes a worse one.</summary>
        public bool Wanted
        {
            get => WantedLevel > 0;
            set
            {
                if (!value) WantedLevel = 0;
                else if (WantedLevel < 1) WantedLevel = 1;
            }
        }

        /// <summary>
        /// The absolute campaign day he went to ground with nobody having laid eyes on
        /// him since; 0 when he is on the street or has just been seen on it.
        ///
        /// A DAY rather than a count of hidden days, for the reason every other clock in
        /// this class is one: a counter drifts across a long soak or a save. A street
        /// sighting sets it back to 0, which is the reset the design asks for - a man who
        /// was spotted on day two has not spent two days hidden, he has spent none.
        /// </summary>
        public int HidingSince;

        /// <summary>
        /// SENT AWAY (GAN-222, FLEE-006). Out of the city until <see cref="BackOnDay"/>:
        /// off the street, off the board and OFF THE PAYROLL - a man in Cleveland is not
        /// drawing an envelope in this one (Wages.WageFor reads this and nothing else).
        ///
        /// The player's third option beside living in a back room and doing his time. It
        /// does not clear anything: he comes back exactly as wanted as he left.
        /// </summary>
        public bool OutOfTown;

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

        /// <summary>
        /// The campaign day the outfit last failed to pay him at all - the first night
        /// of the run he is currently on; 0 when his envelope was full. WAGE-003.
        ///
        /// Not the same thing as <see cref="UnderpaidSince"/>, and both can stand at
        /// once: that one says he is drawing less than he is worth, this one says he
        /// drew NOTHING. A day rather than a count of nights, for the reason every
        /// other clock in this class is one - a counter drifts across a long soak and
        /// a stored day cannot - and cleared the first night he is paid in full, which
        /// is why the run has to be read as (today - this + 1).
        /// </summary>
        public int UnpaidSince;

        /// <summary>The campaign day he last changed rank. An ambitious man who has
        /// been exactly what he is for long enough starts to feel it (LOY-001), and a
        /// day rather than a count for the reason every other clock here is one.</summary>
        public int RankSince;

        /// <summary>He is taking a cut of whatever he handles. Nothing tells the player
        /// this - it shows as thin takes on a block until somebody catches him.</summary>
        public bool Skimming;

        /// <summary>What he has asked to be paid, a day; 0 when he has asked for
        /// nothing. The player answers it - granting it moves his bargain, refusing it
        /// costs loyalty.</summary>
        public int WageDemand;

        /// <summary>
        /// OUT ON BAIL until this absolute campaign day - his own court day (GAN-245);
        /// 0 when he is not. He is a normal man on the street until then: he can be
        /// given orders, he can be arrested again, and on the day itself he is tried on
        /// paper with the rest of his case whether or not he turns up.
        ///
        /// A day rather than a countdown, for the reason every other clock in this
        /// class is one.
        /// </summary>
        public int BailedUntil;

        /// <summary>What the outfit put up to get him out, so a forfeit can be printed
        /// for what it cost. 0 when he was never bailed.</summary>
        public int BailPaid;

        /// <summary>Cases his counsel took to a verdict and won - an acquittal or a
        /// dismissal. Kept ON THE LAWYER rather than derived: the outcome is written on
        /// the DEFENDANT'S rap sheet, and a tally of other men's sheets is not a thing
        /// his own file could ever rebuild after he changed hands.</summary>
        public int CasesWon;

        /// <summary>Cases his counsel lost. See <see cref="CasesWon"/>.</summary>
        public int CasesLost;

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

        /// <summary>Which of the ledger's three marks have already been said out loud
        /// about him. NOT the marks themselves - those are worked out from him every
        /// time they are asked for (<see cref="ManFlags.Of"/>) - only the record of
        /// what the feed has already printed, so crossing a threshold is news once
        /// rather than every midnight afterwards.</summary>
        public ManFlag FlagsAnnounced;

        /// <summary>His record with the city, oldest line first. Dealt with him (every
        /// man on this payroll has a past) and added to as the outfit earns him more.
        /// See <see cref="RapSheet"/>; the personal file prints it.</summary>
        public readonly List<RapEntry> RapSheet = new List<RapEntry>();

        /// <summary>His record with the OUTFIT, oldest line first - jobs he ran, fights
        /// he came out of, the men he answered to, the ranks he held. The rap sheet is
        /// what the city has on him; this is what we have on him, and the two are
        /// printed one under the other on his file. Written only through
        /// <see cref="Career"/>, off events that really happened.</summary>
        public readonly List<CareerEntry> Career = new List<CareerEntry>();

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

        /// <summary>The load boundary for a man's banked practice (RIVAL-010). Nothing
        /// else writes it directly - Practice.Convert is the one road up.</summary>
        public void SetPractice(CharacterAttribute attribute, int value) =>
            practice[(int)attribute] = value < 0 ? 0 : value;

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
