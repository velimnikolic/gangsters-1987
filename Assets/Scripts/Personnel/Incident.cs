namespace LivingCity.Personnel
{
    /// <summary>What a man's character made him do that nobody ordered.</summary>
    public enum IncidentKind
    {
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
                default:
                    return name + " is slowing down.";
            }
        }

        /// <summary>The aging line, which is the one that wants the number: a man
        /// losing a step reads differently at forty-six and at sixty.</summary>
        public static string SlowingLine(string name, int age, string trade) =>
            name + ", " + age + ", is losing his " + trade + ".";
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
