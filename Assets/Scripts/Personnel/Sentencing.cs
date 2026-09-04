using LivingCity.Police;

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

        // Appended so existing serialized values keep their meaning.

        /// <summary>Leaning on a shopkeeper who picked up the telephone about it
        /// (GAN-245). The one deed nobody has to fire a shot for.</summary>
        Extortion,

        /// <summary>Leaning on a man who was going to give evidence. The same band as
        /// the extortion it was meant to bury, because it is the same crime committed
        /// twice.</summary>
        WitnessTampering,

        /// <summary>Firing on the law, whether or not an officer was hit.</summary>
        AssaultOnOfficer,

        /// <summary>Running from an officer who had put the question.</summary>
        Resisting,
    }

    /// <summary>
    /// WHAT HE GETS.
    ///
    /// Caught is no longer guilty: the verdict is a trial now (Police.Verdict), and
    /// this table only answers the second question - how long, once a judge has said
    /// convicted. The answer comes off the deed, off the man's own record, off what
    /// rank he holds and whether the judge has read his name in the paper, off any
    /// counts attached to the case, and off whatever the outfit's lawyer was worth.
    ///
    /// THE BANDS ARE LONGER THAN THEY WERE (GAN-245, "duza za sve"). Every one of them,
    /// not only the new deed: an affray was three to five days, which is a weekend, and
    /// a murder was six to ten. A sentence has to be a thing the player plays around
    /// for a week - bail, a lawyer, a witness leaned on - and none of that is worth
    /// doing to save a man three days.
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
        /// city remembers - and remembers it for twice as long as it used to.</summary>
        public const int EscapeSurcharge = 4;

        /// <summary>Added when the answer at the door was RUN.</summary>
        public const int ResistSurcharge = 2;

        /// <summary>Days at the station before he is put in front of a judge - the leg
        /// the convoy drives (GAN-219, PIPE-002). The sentence is not known before it:
        /// until the verdict lands his sheet says HELD and nothing else.
        ///
        /// ONE of them (ruling of 2026-09-04): he is booked, he sleeps in the cells, and
        /// the next day tick puts him in the car to the courthouse. Bail, the lawyer
        /// and the witnesses are played in that one day - the earlier five made a held
        /// man a week-long absence from the street.</summary>
        public const int DaysToCourt = 1;

        /// <summary>What each extra count attached to a case adds, once convicted -
        /// an open complaint the crew never answered for is not free (GAN-245).</summary>
        public const int ExtraCountDays = 3;

        /// <summary>What a hood's sentence is scaled by, percent: sitna riba. He was
        /// the man holding the door, and the court knows it.</summary>
        public const int HoodPercent = 60;

        /// <summary>No hood goes down for less than this however small the scale makes
        /// him - below it an arrest stops being worth avoiding.</summary>
        public const int HoodFloorDays = 3;

        /// <summary>What a lieutenant whose name is in the paper is scaled by, percent
        /// (<see cref="Notability.Marked"/>). A judge who has read about a man sentences
        /// the man he read about.</summary>
        public const int MarkedLieutenantPercent = 150;

        /// <summary>Days between the verdict and the van to the prison - the SECOND leg
        /// (GAN-237, PIPE-002). A man sentenced this afternoon is held at the court
        /// overnight and driven out in the morning, which is both what happened and the
        /// window the player has to take the road before he goes.</summary>
        public const int DaysToPrison = 1;

        public static bool IsLife(int days) => days >= Life;

        /// <summary>The low end of a deed's band - what the lawyer's cut floors at, and
        /// the figure the "longer for everyone" contract is read off.</summary>
        public static int BandLow(Deed deed) => deed switch
        {
            Deed.CopKilling => Life,
            Deed.Murder => 15,
            Deed.AssaultOnOfficer => 11,
            Deed.Extortion => 8,
            Deed.WitnessTampering => 8,
            Deed.Affray => 6,
            Deed.Resisting => 2,
            _ => 6,
        };

        /// <summary>The high end of a deed's band.</summary>
        public static int BandHigh(Deed deed) => deed switch
        {
            Deed.CopKilling => Life,
            Deed.Murder => 25,
            Deed.AssaultOnOfficer => 14,
            Deed.Extortion => 14,
            Deed.WitnessTampering => 14,
            Deed.Affray => 10,
            Deed.Resisting => 4,
            _ => 10,
        };

        /// <summary>The charge that stays on the front of the file when a fresh act is
        /// added to an existing one. A graver existing deed is never downgraded; on an
        /// equal band the fresh act leads and the old one remains an extra charge.</summary>
        public static Deed PrimaryCharge(Deed existing, Deed fresh)
        {
            var oldHigh = BandHigh(existing);
            var newHigh = BandHigh(fresh);
            if (oldHigh != newHigh) return newHigh > oldHigh ? fresh : existing;

            var oldLow = BandLow(existing);
            var newLow = BandLow(fresh);
            return newLow >= oldLow ? fresh : existing;
        }

        /// <summary>The sentence with nothing on it but the deed and the man's record -
        /// what a defendant of no particular rank, with no counsel and no extra counts,
        /// goes down for.</summary>
        public static int Days(Deed deed, System.Random rng, bool everEscaped) =>
            Days(deed, rng, everEscaped, Rank.Lieutenant, false, 0, 0);

        /// <summary>
        /// The sentence, in days, for one convicted man.
        ///
        /// The order matters and is deliberate: the band is rolled off his own stream,
        /// his counsel argues it down (never below the band's own floor - a lawyer
        /// shortens a sentence, he does not invent a lesser crime), his record and the
        /// counts on the case are added, and only THEN is the whole thing scaled for
        /// what he is. A hood is a hood whatever his lawyer did.
        ///
        /// A dead policeman is life and is not rolled, argued, scaled or surcharged.
        /// </summary>
        public static int Days(Deed deed, System.Random rng, bool everEscaped,
            Rank rank, bool marked, int lawyerSkill, int extraCounts,
            DoorAnswer answer = DoorAnswer.Quiet)
        {
            if (deed == Deed.CopKilling)
                return Life;

            var low = BandLow(deed);
            var days = Roll(rng, low, BandHigh(deed));

            if (lawyerSkill > 0)
            {
                days -= lawyerSkill;
                if (days < low) days = low;
            }

            if (everEscaped)
                days += EscapeSurcharge;
            if (answer == DoorAnswer.Run)
                days += ResistSurcharge;
            if (extraCounts > 0)
                days += extraCounts * ExtraCountDays;

            if (rank == Rank.Hood)
            {
                days = days * HoodPercent / 100;
                if (days < HoodFloorDays) days = HoodFloorDays;
            }
            else if (rank == Rank.Lieutenant && marked)
                days = days * MarkedLieutenantPercent / 100;

            return days;
        }

        static int Roll(System.Random rng, int low, int high) =>
            rng == null ? low : rng.Next(low, high + 1);

        /// <summary>The stream one man's sentence is rolled off - his own, mixed with
        /// the day, so the same man taken twice is not sentenced twice the same. The
        /// trial's own roll comes off this too: one stream per man per day.</summary>
        public static int StreamFor(int rosterSeed, int characterId, int day) =>
            Potential.Mix(rosterSeed + Generation.SeedOffsets.Police + 900,
                unchecked(characterId * 397 + day));

        /// <summary>What goes on the charge line of his sheet.</summary>
        public static string ChargeFor(Deed deed) => deed switch
        {
            Deed.CopKilling => "Murder of a police officer",
            Deed.Murder => "Murder",
            Deed.AssaultOnOfficer => "Assault on a police officer",
            Deed.Resisting => "Resisting arrest",
            Deed.Extortion => "Extortion",
            Deed.WitnessTampering => "Intimidating a witness",
            Deed.Affray => "Affray - discharging firearms in the street",
            _ => "Affray - discharging firearms in the street",
        };

        /// <summary>
        /// What the court wants to let one man out on remand, in 1987 dollars, or 0
        /// where there is no bail at all. A man accused of killing a policeman is not
        /// bailed at any price, which is why the figure is nought rather than large.
        /// (Docs/economy-prices.md, and EconomyPrices.Bail is the door the safe pays
        /// through - this is the table it reads.)
        /// </summary>
        public static int Bail(Deed deed) => deed switch
        {
            Deed.CopKilling => 0,
            Deed.Murder => 25_000,
            Deed.AssaultOnOfficer => 15_000,
            Deed.Affray => 5_000,
            Deed.Extortion => 2_000,
            Deed.WitnessTampering => 2_000,
            Deed.Resisting => 5_000,
            _ => 2_000,
        };

        /// <summary>How it ended, for the outcome column. Free text by design (see
        /// RapSheet) - a city writes what it likes on one of these.</summary>
        public static string Verdict(int days, int outOnDay) =>
            IsLife(days)
                ? "Convicted — life"
                : "Convicted — " + days + (days == 1 ? " day" : " days") +
                  (outOnDay > 0 ? ", out day " + outOnDay : "");

        /// <summary>The line written when the prosecution had nothing to put up.</summary>
        public const string DismissedOutcome = "Case dismissed — no witnesses";

        /// <summary>The line written when he was tried and walked.</summary>
        public const string AcquittedOutcome = "Acquitted";

        /// <summary>The line written when he goes out of a transfer's back door.</summary>
        public const string EscapeOutcome = "Escaped custody";

        /// <summary>The one rap-sheet phrase for a body recovered from a transfer.</summary>
        public const string KilledInTransferOutcome = "Killed in the transfer";

        /// <summary>The line written when he was bailed and never came back.</summary>
        public const string BailForfeitOutcome = "Failed to appear — bail forfeit";
    }
}
