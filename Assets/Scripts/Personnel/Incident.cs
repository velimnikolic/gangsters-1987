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
                default:
                    return name + " is slowing down.";
            }
        }

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
        public static string DefectedLine(string name, int menTaken) => menTaken <= 0
            ? name + " has gone over. Nobody would follow him."
            : menTaken == 1
                ? name + " has gone over, and took one of his men with him."
                : name + " has gone over, and took " + menTaken + " of his men with him.";
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
