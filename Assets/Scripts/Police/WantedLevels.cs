using LivingCity.Personnel;

namespace LivingCity.Police
{
    /// <summary>
    /// A MARKED MAN, and the only thing that unmarks him.
    ///
    /// Running is always an option - but a man who ran is a man the city is looking
    /// for, and the looking is per MAN and never per outfit: there is no wanted level
    /// on a family, because a family cannot be recognised walking down a street.
    ///
    /// Three grades and one cure. He ran from an arrest (1); he came out of the back of
    /// a wrecked transfer (2); he killed a policeman (3). The cure is hidden time -
    /// three clear days for the first, a week for the second, and NOTHING for the
    /// third. No disguises, no bribe that buys it off, no lawyer: a cop-killer in this
    /// city dies, does his life, or lives in a room for the rest of the campaign.
    ///
    /// Every day is an absolute campaign day (Character.HidingSince), never a counter,
    /// so a long soak and a save cannot drift it.
    ///
    /// Pure and free of UnityEngine: the state machine is testable without a street.
    /// </summary>
    public static class WantedLevels
    {
        public const int Fled = 1;
        public const int FreedFromTransfer = 2;
        public const int CopKiller = 3;

        /// <summary>Clear days out of sight that clear each grade. Index by level; the
        /// cop-killer's entry is deliberately impossible rather than large.</summary>
        public const int FledDays = 3;
        public const int FreedDays = 7;

        /// <summary>Never. Not "a very long time" - a number that could be reached is a
        /// promise the design does not make.</summary>
        public const int Never = int.MaxValue;

        public static int DaysToCool(int level) => level switch
        {
            Fled => FledDays,
            FreedFromTransfer => FreedDays,
            _ => Never,
        };

        /// <summary>Marks him, and never downgrades: a man wanted for killing a
        /// policeman who then merely runs from an arrest is still wanted for killing a
        /// policeman.</summary>
        public static void Mark(Character man, int level, int today)
        {
            if (man == null || level <= 0)
                return;
            if (level > man.WantedLevel)
                man.WantedLevel = level > CopKiller ? CopKiller : level;
            // A fresh mark starts him on the street, however long he had been quiet: the
            // city has just been reminded he exists. (The day is taken for the same
            // reason every other call here takes one - so a caller cannot mark a man
            // without knowing what day it is.)
            _ = today;
            man.HidingSince = 0;
        }

        /// <summary>He is off the street and nobody watched him go in.</summary>
        public static void WentToGround(Character man, int today)
        {
            if (man == null || today <= 0 || man.WantedLevel <= 0)
                return;
            if (man.HidingSince <= 0)
                man.HidingSince = today;
        }

        /// <summary>Somebody saw him. The clock goes back to nothing - not back a day,
        /// to nothing: three CLEAR days is what the design asks for.</summary>
        public static void Seen(Character man)
        {
            if (man != null)
                man.HidingSince = 0;
        }

        /// <summary>Days he has been out of sight, as of today.</summary>
        public static int HiddenDays(Character man, int today) =>
            man == null || man.HidingSince <= 0 || today < man.HidingSince
                ? 0
                : today - man.HidingSince;

        /// <summary>
        /// The day turned. A man who has been out of sight long enough for his grade
        /// stops being wanted; a cop-killer never does, whatever he does with his time.
        /// Returns true when this man came clean today, so the caller can say so.
        /// </summary>
        public static bool DayTick(Character man, int today)
        {
            if (man == null || man.WantedLevel <= 0 || today <= 0)
                return false;
            var needed = DaysToCool(man.WantedLevel);
            if (needed == Never)
                return false;
            if (HiddenDays(man, today) < needed)
                return false;
            man.WantedLevel = 0;
            man.HidingSince = 0;
            return true;
        }

        /// <summary>Days a man sent out of the city stays away. Long enough to be a real
        /// decision - a lieutenant off the board for a fortnight is a branch running on
        /// its deputy (Command.EffectiveLieutenant) - and it buys nothing but time.</summary>
        public const int OutOfTownDays = 14;

        /// <summary>
        /// FLEE-006. SEND HIM OUT OF TOWN. He is off the street and off the payroll
        /// until an absolute day, and he comes back exactly as wanted as he left: this
        /// is not a cure, it is somewhere to put a man the city is hunting while the
        /// heat is on somebody else.
        ///
        /// Only for the worst grade. A man wanted for running from an arrest has a cure
        /// already (a few days indoors), and the point of this is the man who has not.
        /// </summary>
        public static bool CanSendAway(Character man) =>
            man != null && man.WantedLevel >= CopKiller &&
            man.Status == CharacterStatus.Active && !man.OutOfTown;

        public static bool SendAway(Character man, int today, int days = OutOfTownDays)
        {
            if (!CanSendAway(man) || today <= 0)
                return false;
            man.OutOfTown = true;
            man.Status = CharacterStatus.Jailed;   // "not on the board" - the one away state
            man.BackOnDay = today + (days < 1 ? 1 : days);
            man.ConditionNote = "Out of town";
            man.HidingSince = 0;   // out of town is not hidden IN town; nothing cools
            return true;
        }

        /// <summary>What the ledger prints beside his name.</summary>
        public static string Word(int level) => level switch
        {
            Fled => "Wanted — fled arrest",
            FreedFromTransfer => "Wanted — escaped custody",
            CopKiller => "Wanted — cop killer",
            _ => "",
        };
    }
}
