using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>What kind of thing happened to him inside the outfit. Coarse on
    /// purpose: the LINE says what happened, this only says how the file should treat
    /// the entry - which of them can never be culled, and which pen it prints in.</summary>
    public enum CareerKind
    {
        /// <summary>He came on the books, and who brought him.</summary>
        Joined,

        /// <summary>A rank change. Never culled: a file that had lost the day a man
        /// was made would be a file that could not answer why he is a lieutenant.</summary>
        Rank,

        /// <summary>He was put under somebody new.</summary>
        Posting,

        /// <summary>Something he did, or something that was done to him - the
        /// incident feed's own events, mirrored into his file.</summary>
        Incident,

        /// <summary>He got better at something, past a whole star.</summary>
        Improved,

        /// <summary>He went down - a bed, a cell, or worse.</summary>
        Condition,

        /// <summary>He came off the books for good.</summary>
        Struck,
    }

    /// <summary>
    /// One line of a man's record with the OUTFIT: the day, what kind of thing it was,
    /// the sentence as it will be printed, and how much it counts for.
    ///
    /// The weight is stamped when the entry is written rather than looked up when it is
    /// read, for the same reason <see cref="Incident"/> carries its own sentence: a
    /// re-tuned table must not silently re-write the history of a campaign already in
    /// progress.
    /// </summary>
    public sealed class CareerEntry
    {
        /// <summary>The campaign day it happened.</summary>
        public int Day;

        public CareerKind Kind;

        /// <summary>The line, as the file sets it.</summary>
        public string Line = "";

        /// <summary>Where, in the street's own words; empty when there was no place
        /// worth naming.</summary>
        public string Where = "";

        /// <summary>What it counts for on the notability score, before decay.</summary>
        public int Weight;
    }

    /// <summary>
    /// A man's history inside the outfit, and the ONE door it is written through.
    ///
    /// This is the parallel to <see cref="RapSheet"/>, deliberately built the same way:
    /// oldest line first, appended as things happen, printed on the personal file under
    /// the record the city keeps. The city knows what he was charged with; the outfit
    /// knows what he actually did, and after ten hours the second list is the one that
    /// makes a name mean something.
    ///
    /// Every entry originates in a real event record - an <see cref="Incident"/>, an
    /// <see cref="Improvement"/>, a rank change made through <see cref="RosterOps"/>.
    /// Nothing writes a free-floating string at a call site, which is what keeps the
    /// paper, the feed and the file from disagreeing about what happened.
    ///
    /// It is also the spine <see cref="Notability"/> folds: score is a decayed sum over
    /// this list plus whatever marks stand against him today. One log, two readers.
    ///
    /// Pure and free of UnityEngine.
    /// </summary>
    public static class Career
    {
        /// <summary>
        /// How many lines a file keeps. A five-year campaign is eighteen hundred days
        /// and would otherwise turn every man's file into an archive nobody reads to
        /// the bottom of; twenty-four is about a screen of the personal file at the
        /// widths the book is set at, which is the real constraint.
        ///
        /// Rank changes and the joining line are exempt and never counted against it -
        /// see <see cref="Cull"/>. Everything else competes on weight, so what survives
        /// a long career is what mattered in it.
        /// </summary>
        public const int Kept = 24;

        /// <summary>A rank change. The one thing the file may never forget.</summary>
        public const int RankWeight = 70;

        public const int JoinedWeight = 20;
        public const int PostingWeight = 10;
        public const int ImprovedWeight = 25;
        public const int ConditionWeight = 45;
        public const int StruckWeight = 80;

        /// <summary>
        /// Writes one line. Everything else in this class comes through here, and
        /// nothing outside it does - a caller who cannot name a record to write from
        /// has no business writing history.
        /// </summary>
        static void Write(Character man, int day, CareerKind kind, int weight,
            string line, string where = "")
        {
            if (man == null || string.IsNullOrEmpty(line))
                return;

            man.Career.Add(new CareerEntry
            {
                Day = day,
                Kind = kind,
                Line = line,
                Where = where ?? "",
                Weight = weight,
            });
            Cull(man);
        }

        /// <summary>The day he came on the books, and who brought him in.</summary>
        public static void Joined(Character man, int day, string broughtBy) =>
            Write(man, day, CareerKind.Joined, JoinedWeight,
                CareerText.Joined(broughtBy));

        /// <summary>
        /// The day his file says he came on the books; 0 for a man dealt without one -
        /// the founding fixture, a hand-built test character, a rival house's roster.
        /// Read rather than stored, which is what lets the wage table price service
        /// (Outfit.Wages.TenureBonus) with nothing new on the Character at all.
        ///
        /// The FIRST joining line wins. A man struck off and taken on again keeps the
        /// service he actually did; nothing in the design pays him twice for signing.
        /// </summary>
        public static int JoinedDay(Character man)
        {
            if (man == null)
                return 0;
            for (var i = 0; i < man.Career.Count; i++)
                if (man.Career[i].Kind == CareerKind.Joined)
                    return man.Career[i].Day;
            return 0;
        }

        /// <summary>Made, or taken back down. Never culled.</summary>
        public static void RankChanged(Character man, int day, Rank to, string reason) =>
            Write(man, day, CareerKind.Rank, RankWeight, CareerText.Rank(to, reason));

        /// <summary>Put under somebody new - the relationship the whole loyalty layer
        /// hangs off, so the file says when it started.</summary>
        public static void Posted(Character man, int day, string superior) =>
            Write(man, day, CareerKind.Posting, PostingWeight,
                CareerText.Posted(superior));

        /// <summary>
        /// One of the feed's own events, mirrored into his file. The sentence is the
        /// incident's - already rendered, never re-worded here - so his file and the
        /// paper carry the same words about the same night.
        ///
        /// A rank change is skipped: it already went on his file as a
        /// <see cref="CareerKind.Rank"/> entry when it happened, which is the entry
        /// that may never be culled, and printing the paper's sentence about it
        /// underneath would say the same thing twice in two registers.
        /// </summary>
        public static void FromIncident(Character man, in Incident incident)
        {
            if (incident.Kind == IncidentKind.Promoted ||
                incident.Kind == IncidentKind.Demoted ||
                incident.Kind == IncidentKind.Defected)
                return;
            Write(man, incident.Day, CareerKind.Incident,
                Notability.WeightOf(incident.Kind), incident.Line, incident.Where);
        }

        /// <summary>
        /// A rise, but only one that crossed a WHOLE star. Half-steps land most weeks
        /// and would fill the file with a line nobody would read; a whole star is the
        /// thing a man would say about himself.
        /// </summary>
        public static void Improved(Character man, int day, in Improvement rise)
        {
            if (rise.HalfSteps % 2 != 0)
                return;
            Write(man, day, CareerKind.Improved, ImprovedWeight,
                CareerText.Improved(rise.Attribute, rise.HalfSteps));
        }

        /// <summary>He went down. The condition note is written when it happens
        /// (RosterOps.Hospitalize / Jail); this mirrors the MOMENT into the history, so
        /// a file still says he was shot in the spring after he is back on his
        /// feet.</summary>
        public static void WentDown(Character man, int day, CharacterStatus status,
            string note) =>
            Write(man, day, CareerKind.Condition, ConditionWeight,
                CareerText.WentDown(status, note));

        /// <summary>
        /// Off the books for good.
        ///
        /// <paramref name="story"/> replaces the clerk's stock sentence when the
        /// caller knows something better. A defection goes out through the desertion
        /// door - one door, so equipment, wages and posts all settle the way they
        /// always did - but "Gone. Did not come back." is what the file says about a
        /// man who ran from a fight, and a man who walked out behind his lieutenant did
        /// not run from anything. The weight travels with it for the same reason: going
        /// over is the loudest thing that happens on this payroll and must not be culled
        /// as though it were a desertion.
        /// </summary>
        public static void StruckOff(Character man, int day, CharacterStatus status,
            string story = "", int weight = 0) =>
            Write(man, day, CareerKind.Struck, weight > 0 ? weight : StruckWeight,
                CareerText.StruckOff(status, story));

        /// <summary>
        /// Holds the file to its length. Rank changes are kept whatever happens - they
        /// are the skeleton the rest hangs on - and so is the joining line, because the
        /// wage table prices a man's SERVICE off it (Outfit.Wages.TenureBonus): a file
        /// that had culled the day he came on the books would quietly cut a ten-year
        /// man's envelope back to a recruit's. Of the rest the LEAST notable go first,
        /// oldest breaking the tie, so what a long career keeps is what mattered in it
        /// rather than merely what happened last.
        ///
        /// Order is never disturbed: entries are removed, never re-sorted, and the file
        /// still reads forward.
        /// </summary>
        static void Cull(Character man)
        {
            var cullable = 0;
            for (var i = 0; i < man.Career.Count; i++)
                if (Cullable(man.Career[i].Kind))
                    cullable++;
            if (cullable <= Kept)
                return;

            var toDrop = cullable - Kept;
            for (var n = 0; n < toDrop; n++)
            {
                var worst = -1;
                for (var i = 0; i < man.Career.Count; i++)
                {
                    var entry = man.Career[i];
                    if (!Cullable(entry.Kind))
                        continue;
                    if (worst < 0 || entry.Weight < man.Career[worst].Weight)
                        worst = i;
                }
                if (worst < 0)
                    return;
                man.Career.RemoveAt(worst);
            }
        }

        /// <summary>The two lines a file may never lose: what rank he holds and when
        /// he came on the books. Everything else competes on weight.</summary>
        static bool Cullable(CareerKind kind) =>
            kind != CareerKind.Rank && kind != CareerKind.Joined;
    }

    /// <summary>
    /// Every sentence a career entry can carry, in ONE place - the same discipline
    /// <see cref="IncidentText"/> keeps, and for the same reason: the file, the roster
    /// aside and any future summary all print what this builds, so a re-wording cannot
    /// make two pages disagree about a man's life.
    /// </summary>
    public static class CareerText
    {
        public static string Joined(string broughtBy) =>
            string.IsNullOrEmpty(broughtBy)
                ? "Came on the books."
                : "Came on the books - brought in by " + broughtBy + ".";

        public static string Rank(Rank to, string reason)
        {
            var made = to switch
            {
                Personnel.Rank.Lieutenant => "Made a lieutenant",
                Personnel.Rank.Boss => "Took the outfit",
                _ => "Back to a hood",
            };
            return string.IsNullOrEmpty(reason) ? made + "." : made + " - " + reason + ".";
        }

        public static string Posted(string superior) =>
            string.IsNullOrEmpty(superior)
                ? "Left without a post."
                : "Put under " + superior + ".";

        public static string Improved(CharacterAttribute attribute, int halfSteps) =>
            "Up to " + (halfSteps / 2) + " stars at " + TradeWord(attribute) + ".";

        public static string WentDown(CharacterStatus status, string note)
        {
            var what = status switch
            {
                CharacterStatus.Hospitalized => "Went down",
                CharacterStatus.Jailed => "Taken and held",
                _ => "Off his feet",
            };
            return string.IsNullOrEmpty(note) ? what + "." : what + " - " + note + ".";
        }

        public static string StruckOff(CharacterStatus status, string story = "")
        {
            if (!string.IsNullOrEmpty(story))
                return story;
            return status switch
            {
                CharacterStatus.Dead => "Killed.",
                CharacterStatus.Deserted => "Gone. Did not come back.",
                _ => "Off the books.",
            };
        }

        /// <summary>The lieutenant's own line the day he goes over. It carries the
        /// COUNT, because how expensive losing him was is the whole point of the
        /// Leadership arithmetic that decided it.</summary>
        public static string WentOver(int menTaken) => menTaken <= 0
            ? "Went over to another family, and went alone."
            : menTaken == 1
                ? "Went over to another family, and took one man with him."
                : "Went over to another family, and took " + menTaken +
                  " men with him.";

        /// <summary>And the line on the file of each man who went out behind him. This
        /// is what stops a defection reading as a desertion on the men it actually
        /// happened to.</summary>
        public static string WalkedOutWith(string lieutenantName) =>
            "Walked out behind " + lieutenantName + ".";

        /// <summary>The trade in the word a man would use about himself, rather than
        /// the ledger's column heading. Kept here rather than reaching into the UI
        /// layer, which Personnel is not allowed to know about.</summary>
        public static string TradeWord(CharacterAttribute attribute) => attribute switch
        {
            CharacterAttribute.Combat => "the work",
            CharacterAttribute.Awareness => "what he notices",
            CharacterAttribute.Stealth => "going unseen",
            CharacterAttribute.Driving => "the wheel",
            CharacterAttribute.Streetwise => "the street",
            CharacterAttribute.Leadership => "leading men",
            CharacterAttribute.Organization => "running things",
            CharacterAttribute.StreetAuthority => "his name",
            CharacterAttribute.Persuasion => "talking",
            CharacterAttribute.Intimidation => "leaning",
            _ => "who he knows",
        };
    }
}
