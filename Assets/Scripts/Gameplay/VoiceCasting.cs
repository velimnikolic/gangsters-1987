using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Personnel;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Which actor a man speaks in.
    ///
    /// A MAN'S VOICE IS HIS OWN, on the same terms as his coat (GangLooks): settled the
    /// first time he opens his mouth, written on him (<see cref="Character.Voice"/>) and
    /// carried in the save from then on. The tables below say what a man of that age and
    /// temper is DEALT when he is new, never what he must sound like later - a promotion
    /// is a change of rank, not of larynx.
    ///
    /// The roll is weighted, never forced. A quiet young man in the gravel bank is a face
    /// the player remembers, so every street bank keeps a floor weight and the tags only
    /// tilt the deal.
    /// </summary>
    public static class VoiceCasting
    {
        /// <summary>Under this, a man reads young; from PrimeTo up, he reads old. Chosen
        /// off the roster's own draw (Aging.MinAge 21, MaxAge 62): the bands come out
        /// roughly a third each, so no bank sits idle.</summary>
        const int YoungTo = 30;
        const int PrimeTo = 50;

        /// <summary>Trait points above which a man is loud, or is flat. 0-100 scale, and
        /// deliberately not the midpoint - half the outfit tagged "hot" would make the
        /// hot banks the ordinary ones.</summary>
        const int HotFloor = 62;
        const int SteadyFloor = 62;

        const int MatchAge = 4;
        const int AdjacentAge = 2;
        const int MatchTemper = 3;
        const int Floor = 1;

        /// <summary>The seed stream this casting draws on. Its own number so a change to
        /// the voice tables cannot move any other roll a man is made of.</summary>
        const int VoiceSeed = 29_000;

        static readonly List<string> Banks = new List<string>(16);

        /// <summary>The bank this man speaks in, casting him if he has never spoken. Null
        /// only when nothing has been recorded at all, which is silence, not an error.
        /// </summary>
        public static string BankFor(Character man, Roster roster = null)
        {
            if (man == null)
                return null;

            var db = VoiceDatabase.Instance;
            if (db == null)
                return null;

            db.CollectBankIds(Banks);
            if (Banks.Count == 0)
                return null;

            // Already cast, and the bank is still in the folder: that is his voice.
            if (!string.IsNullOrEmpty(man.Voice) && Banks.Contains(man.Voice))
                return man.Voice;

            man.Voice = Deal(man, db, roster);
            return man.Voice;
        }

        /// <summary>
        /// A voice for a man who is on nobody's books - a rival mobster, an officer of the
        /// law. They cry out when they are shot exactly as our men do, and a fight where
        /// only our side makes a sound is half a fight; but they have no Character to write
        /// a bank onto, so the bank is dealt straight off whatever number identifies the
        /// body and never stored. Stable for as long as that body lives, which is as long
        /// as it needs to be.
        /// </summary>
        public static string BankForSeed(int seed)
        {
            var db = VoiceDatabase.Instance;
            if (db == null)
                return null;

            db.CollectBankIds(Banks);
            if (Banks.Count == 0)
                return null;

            var rng = new System.Random(Potential.Mix(seed, VoiceSeed + 2));
            return Banks[rng.Next(Banks.Count)];
        }

        /// <summary>The same pitch trim for a man with no roster entry.</summary>
        public static float PitchForSeed(int seed)
        {
            var rng = new System.Random(Potential.Mix(seed, VoiceSeed + 3));
            return 0.94f + rng.Next(0, 13) * 0.01f;
        }

        static string Deal(Character man, VoiceDatabase db, Roster roster)
        {
            var year = roster != null && roster.Year > 0 ? roster.Year : RosterSeeder.CalendarStartYear;
            var age = Aging.AgeOn(man, year);
            var band = age <= 0 ? VoiceAge.Prime
                : age < YoungTo ? VoiceAge.Young
                : age < PrimeTo ? VoiceAge.Prime
                : VoiceAge.Old;

            var hot = Personality.Get(man, PersonalityTrait.Temper) >= HotFloor;
            var steady = Personality.Get(man, PersonalityTrait.Discipline) >= SteadyFloor;

            var total = 0;
            for (var i = 0; i < Banks.Count; i++)
                total += Weight(db.Find(Banks[i]), band, hot, steady);

            // Every bank floors at Floor, so total is never 0 while there is a bank at all.
            var rng = new System.Random(Potential.Mix(man.Id, VoiceSeed));
            var roll = rng.Next(total);
            for (var i = 0; i < Banks.Count; i++)
            {
                roll -= Weight(db.Find(Banks[i]), band, hot, steady);
                if (roll < 0)
                    return Banks[i];
            }
            return Banks[Banks.Count - 1];
        }

        static int Weight(VoiceDatabase.Bank bank, VoiceAge band, bool hot, bool steady)
        {
            if (bank == null)
                return 0;

            var weight = Floor;

            if (bank.age == band)
                weight += MatchAge;
            else if (Adjacent(bank.age, band))
                weight += AdjacentAge;

            if (hot && bank.temper == VoiceTemper.Hot) weight += MatchTemper;
            if (steady && bank.temper == VoiceTemper.Steady) weight += MatchTemper;

            return weight;
        }

        /// <summary>Prime sits between the other two, so it neighbours both and they
        /// neighbour only it - a young man may be dealt a prime voice, never an old one.
        /// </summary>
        static bool Adjacent(VoiceAge a, VoiceAge b) =>
            a == VoiceAge.Prime || b == VoiceAge.Prime;

        /// <summary>
        /// How far off true his voice is pitched, as a multiplier for AudioSource.pitch.
        ///
        /// Derived from his id and never stored: it is a knob on the same recording, not a
        /// fact about the man, and if the range is ever re-tuned every man should move with
        /// it. Eight banks and a hand's width of pitch either side is what makes a family of
        /// forty sound like forty men rather than eight.
        /// </summary>
        public static float PitchFor(Character man)
        {
            if (man == null)
                return 1f;
            var rng = new System.Random(Potential.Mix(man.Id, VoiceSeed + 1));
            return 0.94f + rng.Next(0, 13) * 0.01f;    // 0.94 .. 1.06
        }
    }
}
