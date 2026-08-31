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
