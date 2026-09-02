namespace LivingCity.Personnel
{
    /// <summary>What a man was taken for. The deed, not the paperwork - the charge text
    /// on his sheet is written from it.</summary>
    public enum Deed
    {
        /// <summary>Guns off in the street and nobody down.</summary>
        Affray,

        /// <summary>A shooting with a body in it, gangland or bystander.</summary>
        Murder,

        /// <summary>He killed a policeman. There is no other kind of this.</summary>
        CopKilling,
    }

    /// <summary>
    /// WHAT HE GETS. Caught is guilty - there is no acquittal in this version, because
    /// an acquittal roll would make the whole arrest a coin toss the player watches -
    /// so the only question is how long, and the answer comes off the deed and off the
    /// man's own record.
    ///
    /// Before this, every arrest was three days flat (DemoCrews.HeldDays), which meant
    /// shooting up a shopfront and shooting a policeman cost the outfit exactly the
    /// same. They do not cost the same now.
    ///
    /// Days everywhere, absolute campaign days at the call sites; the rap sheet gets
    /// the words. Pure and free of UnityEngine like the rest of Personnel.
    /// </summary>
    public static class Sentencing
    {
        /// <summary>
        /// LIFE. An explicit day number no campaign reaches, and deliberately NOT
        /// int.MaxValue: the day tick compares <see cref="Character.BackOnDay"/> against
        /// today, and a call site that added a surcharge to int.MaxValue would overflow
        /// into a negative day and release a cop-killer the same night.
        /// </summary>
        public const int Life = 1_000_000_000;

        /// <summary>Days added for a man who has been out of custody once already. The
        /// city remembers.</summary>
        public const int EscapeSurcharge = 2;

        /// <summary>Days at the station before he is put in front of a judge - the leg
        /// the convoy drives (GAN-219, PIPE-002). The sentence is not known before it:
        /// until the verdict lands his sheet says HELD and nothing else.</summary>
        public const int DaysToCourt = 2;

        public static bool IsLife(int days) => days >= Life;

        /// <summary>
        /// The sentence, in days.
        ///
        /// A shootout is three to five, rolled off the man's own stream so two men taken
        /// at the same corner do not get the same number; a body doubles it; a dead
        /// policeman is life and is not rolled at all.
        ///
        /// NOTE for the designer: only the shootout band (3-5) and the cop-killer's life
        /// are the epic's own figures. The murder band below is DERIVED from them - twice
        /// the shootout - rather than dealt from anywhere, and is the one number here
        /// waiting on a word.
        /// </summary>
        public static int Days(Deed deed, System.Random rng, bool everEscaped)
        {
            if (deed == Deed.CopKilling)
                return Life;

            var days = deed == Deed.Murder
                ? Roll(rng, 6, 10)
                : Roll(rng, 3, 5);
            if (everEscaped)
                days += EscapeSurcharge;
            return days;
        }

        static int Roll(System.Random rng, int low, int high) =>
            rng == null ? low : rng.Next(low, high + 1);

        /// <summary>The stream one man's sentence is rolled off - his own, mixed with
        /// the day, so the same man taken twice is not sentenced twice the same.</summary>
        public static int StreamFor(int rosterSeed, int characterId, int day) =>
            Potential.Mix(rosterSeed + Generation.SeedOffsets.Police + 900,
                unchecked(characterId * 397 + day));

        /// <summary>What goes on the charge line of his sheet.</summary>
        public static string ChargeFor(Deed deed) => deed switch
        {
            Deed.CopKilling => "Murder of a police officer",
            Deed.Murder => "Murder",
            _ => "Affray - discharging firearms in the street",
        };

        /// <summary>How it ended, for the outcome column. Free text by design (see
        /// RapSheet) - a city writes what it likes on one of these.</summary>
        public static string Verdict(int days, int outOnDay) =>
            IsLife(days)
                ? "Convicted — life"
                : "Convicted — " + days + (days == 1 ? " day" : " days") +
                  (outOnDay > 0 ? ", out day " + outOnDay : "");

        /// <summary>The line written when he goes out of a transfer's back door.</summary>
        public const string EscapeOutcome = "Escaped custody";
    }
}
