namespace LivingCity.Personnel
{
    /// <summary>What a man's character made him do that nobody ordered.</summary>
    public enum IncidentKind
    {
        /// <summary>One family said something to another - a warning, a threat, or a
        /// bill. It is printed in both books: theirs so they know, ours so the player
        /// can read what his own house said (RIVAL-007).</summary>
        AWordBetweenHouses,

        /// <summary>He went to pieces when it turned dangerous and did nothing at
        /// all.</summary>
        Froze,

        /// <summary>He ran, and did not come back.</summary>
        Fled,

        /// <summary>A job that needed no shooting ended in shooting.</summary>
        Escalated,

        /// <summary>It happened - louder, later, or messier than it was ordered.</summary>
        Deviated,

        /// <summary>Somebody else's envelope started looking reasonable.</summary>
        TookRivalMoney,

        /// <summary>He stopped being quiet about being underpaid.</summary>
        DemandedARaise,

        /// <summary>Caught with his hand in the count.</summary>
        CaughtSkimming,

        /// <summary>The years took something off him.</summary>
        SlowingDown,

        /// <summary>He stood in front of the Don and it killed him.</summary>
        DiedOnTheDetail,

        /// <summary>He stood in front of the Don and lived.</summary>
        StoppedIt,

        /// <summary>His loyalty crossed down through the watch band.</summary>
        BearsWatching,

        /// <summary>A lieutenant walked, and took men with him.</summary>
        Defected,

        /// <summary>He was made a lieutenant.</summary>
        Promoted,

        /// <summary>He was taken back down to a hood.</summary>
        Demoted,

        /// <summary>His command trades crossed the line: he could run a crew.</summary>
        ReadyForACrew,

        /// <summary>He can shoot, he will go, and he does as he is told.</summary>
        AGunForHire,

        /// <summary>Ambition over the line and loyalty under it - the red flag.</summary>
        NotToBeTrusted,

        /// <summary>The safe could not cover the night's wages and men went home with
        /// nothing. Appended, like every kind before it, so serialized values keep
        /// their meaning.</summary>
        PayrollShort,

        // ------------------------------------------------------------- GAN-245
        // The law's own paper. Appended, like everything before it.

        /// <summary>A shopkeeper who was leaned on picked up the telephone.</summary>
        ComplaintRung,

        /// <summary>An officer stood at the door with a notebook and nobody to
        /// arrest.</summary>
        StatementTaken,

        /// <summary>A case was opened, with a list of names on it.</summary>
        CaseOpened,

        /// <summary>A witness was leaned on and will not stand up.</summary>
        WitnessWithdrawn,

        /// <summary>A witness was killed.</summary>
        WitnessKilled,

        /// <summary>The outfit put money up and got a man out until his day.</summary>
        BailPosted,

        /// <summary>He did not turn up, and the money is gone.</summary>
        BailForfeit,

        /// <summary>The court found against him.</summary>
        Convicted,

        /// <summary>The court found for him.</summary>
        Acquitted,

        /// <summary>The prosecution had nobody to put up and it was thrown out.</summary>
        CaseDismissed,

        /// <summary>The boss had a man inside and sold him.</summary>
        CutLoose,

        /// <summary>His lieutenant handed him the crew's collection bag (GAN-262) - he
        /// walks the rounds now and stands at the front between them.</summary>
        BagHanded,
    }

    /// <summary>
    /// The sentence, for every kind of incident, in ONE place. The paper, the ledger
    /// and the man's own file all print the string this builds; none of them writes its
    /// own, which is what stops a re-worded line from making three pages disagree about
    /// what happened.
    ///
    /// 1987 tabloid brevity: one sentence, the man named, the street named when there
    /// is one, and no adjectives the wire would have cut.
    /// </summary>
    public static class IncidentText
    {
        public static string Line(IncidentKind kind, string name, string where)
        {
            var place = string.IsNullOrEmpty(where) ? "" : " at " + where;
            switch (kind)
            {
                case IncidentKind.AWordBetweenHouses:
                    // The whole sentence was written by the house that said it: which
                    // family, and what it said. Nothing here to add to it.
                    return name;
                case IncidentKind.Froze:
                    return name + " froze when it started" + place + ".";
                case IncidentKind.Fled:
                    return name + " ran" + place + " and did not come back.";
                case IncidentKind.Escalated:
                    return "A job" + place + " ended in gunfire nobody ordered - " +
                           name + " lost his temper.";
                case IncidentKind.Deviated:
                    return name + " did the job" + place +
                           ", but not the way it was written.";
                case IncidentKind.TookRivalMoney:
                    return "Word is " + name + " has been drinking with men who are " +
                           "not ours.";
                case IncidentKind.DemandedARaise:
                    return name + " wants his envelope brought up to the rate.";
                case IncidentKind.CaughtSkimming:
                    return name + " has been taking a cut off the top" + place + ".";
                case IncidentKind.DiedOnTheDetail:
                    return "They came for the Don" + place + ". " + name +
                           " was in the way, and did not get up.";
                case IncidentKind.StoppedIt:
                    return "They came for the Don" + place + ". " + name +
                           " took it instead, and is in a bed.";
                case IncidentKind.BearsWatching:
                    return name + " bears watching.";
                case IncidentKind.Defected:
                    return name + " has gone over, and he did not go alone.";
                case IncidentKind.Promoted:
                    return name + " has a crew of his own.";
                case IncidentKind.Demoted:
                    return name + " has been taken back down, and his crew broken up.";
                case IncidentKind.ReadyForACrew:
                    return name + " is ready for a crew of his own.";
                case IncidentKind.AGunForHire:
                    return name + " can shoot, and he does not lose his head.";
                case IncidentKind.NotToBeTrusted:
                    return name + " wants more than he has, and he no longer thinks " +
                           "it is coming from us.";
                case IncidentKind.PayrollShort:
                    return name + " went home with an empty envelope.";
                case IncidentKind.ComplaintRung:
                    return name + " rang the precinct about our men.";
                case IncidentKind.StatementTaken:
                    return "An officer took a statement at " + name +
                           " and found nobody to take in.";
                case IncidentKind.CaseOpened:
                    return "The city has opened a case over " + name +
                           ", and our names are on it.";
                case IncidentKind.WitnessWithdrawn:
                    return name + " has remembered nothing after all.";
                case IncidentKind.WitnessKilled:
                    return name + " will not be giving evidence.";
                case IncidentKind.BailPosted:
                    return name + " is out until his day in court.";
                case IncidentKind.BailForfeit:
                    return name + " did not appear. The money is gone and the city " +
                           "is looking for him.";
                case IncidentKind.Convicted:
                    return "The court has passed sentence on " + name + ".";
                case IncidentKind.Acquitted:
                    return name + " walked out of it.";
                case IncidentKind.CaseDismissed:
                    return name + " walked: nobody would give evidence" + place + ".";
                case IncidentKind.CutLoose:
                    return name + " was cut loose while he was inside.";
                case IncidentKind.BagHanded:
                    return name + " was handed the bag.";
                default:
                    return name + " is slowing down.";
            }
        }

        /// <summary>The bag line with both names on it - who gave and who carries.</summary>
        public static string BagHandedLine(string lieutenant, string hood) =>
            (string.IsNullOrEmpty(lieutenant) ? "His lieutenant" : lieutenant) +
            " handed the bag to " + hood + ".";

        /// <summary>
        /// The short-payroll line: the outfit's own, not one man's, so it is the third
        /// of the sentences that wants a number rather than a name. One line a night,
        /// however many envelopes were empty - a feed that printed a line per man
        /// would bury the night that caused them.
        /// </summary>
        public static string PayrollShortLine(int menUnpaid, int owed) =>
            menUnpaid <= 0
                ? "The envelopes were short."
                : menUnpaid == 1
                    ? "The envelopes were short: one man went home unpaid, $" +
                      owed + " owed."
                    : "The envelopes were short: " + menUnpaid +
                      " men went home unpaid, $" + owed + " owed.";

        /// <summary>The aging line, which is the one that wants the number: a man
        /// losing a step reads differently at forty-six and at sixty.</summary>
        public static string SlowingLine(string name, int age, string trade) =>
            name + ", " + age + ", is losing his " + trade + ".";

        /// <summary>
        /// The defection line, the other one that wants a number. A lieutenant going
        /// over is expensive in proportion to how many men went with him, and that
        /// figure is his Leadership - the thing he was promoted FOR. A page that only
        /// said "he did not go alone" left the player to notice four missing names on
        /// his own.
        /// </summary>
        /// <summary>
        /// A lieutenant going over, and where he went. The house is named when the
        /// caller knows it; an empty name prints the words the paper printed before
        /// anybody worked out whose door he knocked on.
        /// </summary>
        public static string DefectedLine(string name, int menTaken, string family = "")
        {
            var to = string.IsNullOrEmpty(family)
                ? " has gone over"
                : " has gone over to the " + family + " family";
            return menTaken <= 0
                ? name + to + ". Nobody would follow him."
                : menTaken == 1
                    ? name + to + ", and took one of his men with him."
                    : name + to + ", and took " + menTaken + " of his men with him.";
        }
    }

    /// <summary>
    /// One thing that happened to one man, written when it happened and never
    /// re-derived. Carries the sentence already rendered, so the paper prints it and
    /// the notability score reads the FIELDS - nothing downstream ever parses the
    /// English back into facts.
    ///
    /// This is the record that makes the whole personality design work: the player
    /// learns who his men are from reports of what they did, not from a stat screen.
    /// </summary>
    public readonly struct Incident
    {
        public readonly int CharacterId;
        public readonly string Name;
        public readonly IncidentKind Kind;

        /// <summary>The campaign day it happened.</summary>
        public readonly int Day;

        /// <summary>Where, in the street's own words - the job's target label. Empty
        /// when the work had no place worth naming.</summary>
        public readonly string Where;

        /// <summary>Police attention it drew on top of the job's own.</summary>
        public readonly int Heat;

        /// <summary>The line, as the paper would set it.</summary>
        public readonly string Line;

        public Incident(int characterId, string name, IncidentKind kind, int day,
            string where, int heat, string line)
        {
            CharacterId = characterId;
            Name = name;
            Kind = kind;
            Day = day;
            Where = where ?? "";
            Heat = heat;
            Line = line ?? "";
        }
    }
}
