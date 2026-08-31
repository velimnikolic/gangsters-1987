namespace LivingCity.Personnel
{
    /// <summary>
    /// The moments personality decides. Courage decides what a man does when it turns
    /// dangerous; Temper decides whether a job that needed no shootout ends in one;
    /// Discipline decides whether the thing that happened is the thing that was
    /// ordered.
    ///
    /// Every check that fires produces an <see cref="Incident"/> - a line the player
    /// reads. There is deliberately no check here whose only output is a modifier: a
    /// number that moved for a reason nobody was told is exactly the thing this whole
    /// design exists to avoid.
    ///
    /// The rolls come off a caller-supplied System.Random, seeded per job by the Outfit
    /// layer, so the same campaign at the same seed produces the same men going to
    /// pieces on the same nights.
    ///
    /// Pure, free of UnityEngine and of the Outfit layer: what to DO about a man who
    /// ran is the caller's business, and the caller is the only one who knows where he
    /// was standing.
    /// </summary>
    public static class PersonalityChecks
    {
        /// <summary>Above this much Courage a man is never checked at all - the roll
        /// exists to find the men who cannot do this work, not to make every job a
        /// lottery for everybody.</summary>
        public const int CourageFloor = 40;

        /// <summary>Percent chance of the check firing, per point BELOW the floor. At
        /// two per point a Courage-30 man goes to pieces on a fifth of the violent work
        /// he is sent on, and a Courage-10 man on most of it.</summary>
        public const int CourageRiskPerPoint = 2;

        /// <summary>Of the men who fail the check, how many run rather than freeze. A
        /// freeze is the common failure and costs the outfit a job; running costs it
        /// the man.</summary>
        public const int FleePercent = 25;

        /// <summary>Below this much Temper nothing is ever provoked out of him.</summary>
        public const int TemperCeiling = 60;

        /// <summary>Percent chance per point ABOVE the ceiling.</summary>
        public const int TemperRiskPerPoint = 1;

        /// <summary>What a collection that ended in gunfire draws on top of the job's
        /// own attention.</summary>
        public const int EscalationHeat = 6;

        /// <summary>Above this much Discipline the job happens the way it was
        /// ordered.</summary>
        public const int DisciplineFloor = 40;

        /// <summary>Percent chance per point below it.</summary>
        public const int DisciplineRiskPerPoint = 1;

        /// <summary>What a job done louder than it was ordered draws.</summary>
        public const int DeviationHeat = 2;

        /// <summary>
        /// He has been committed to violence. A man under the floor may go to pieces,
        /// and a quarter of those who do run instead of freezing.
        /// </summary>
        public static bool TryCourage(Character man, System.Random rng, int day,
            string where, out Incident incident)
        {
            incident = default;
            if (!Fires(man, rng, Personality.Get(man, PersonalityTrait.Courage),
                    CourageFloor, CourageRiskPerPoint, below: true))
                return false;

            var ran = rng.Next(100) < FleePercent;
            var kind = ran ? IncidentKind.Fled : IncidentKind.Froze;
            incident = new Incident(man.Id, man.FullName, kind, day, where, 0,
                Line(kind, man.FullName, where));
            return true;
        }

        /// <summary>
        /// He has been provoked - an owner who would not pay, a rival on the same
        /// corner. A man over the ceiling may finish the conversation with a gun.
        /// </summary>
        public static bool TryTemper(Character man, System.Random rng, int day,
            string where, out Incident incident)
        {
            incident = default;
            if (!Fires(man, rng, Personality.Get(man, PersonalityTrait.Temper),
                    TemperCeiling, TemperRiskPerPoint, below: false))
                return false;

            incident = new Incident(man.Id, man.FullName, IncidentKind.Escalated, day,
                where, EscalationHeat,
                Line(IncidentKind.Escalated, man.FullName, where));
            return true;
        }

        /// <summary>
        /// The job had a choice in it - a route, an hour, how hard to lean. A man under
        /// the floor takes the choice himself.
        /// </summary>
        public static bool TryDiscipline(Character man, System.Random rng, int day,
            string where, out Incident incident)
        {
            incident = default;
            if (!Fires(man, rng, Personality.Get(man, PersonalityTrait.Discipline),
                    DisciplineFloor, DisciplineRiskPerPoint, below: true))
                return false;

            incident = new Incident(man.Id, man.FullName, IncidentKind.Deviated, day,
                where, DeviationHeat,
                Line(IncidentKind.Deviated, man.FullName, where));
            return true;
        }

        /// <summary>The shared roll: how far past the line he is, times the risk per
        /// point, against a hundred. A man on the right side of the line is never
        /// rolled for at all.</summary>
        static bool Fires(Character man, System.Random rng, int value, int line,
            int perPoint, bool below)
        {
            if (man == null || rng == null || man.Gone)
                return false;

            var past = below ? line - value : value - line;
            if (past <= 0)
                return false;

            var chance = past * perPoint;
            return rng.Next(100) < chance;
        }

        /// <summary>The sentence for a check that fired. One line only, and it is
        /// <see cref="IncidentText"/>'s - kept as a name the checks read against.</summary>
        public static string Line(IncidentKind kind, string name, string where) =>
            IncidentText.Line(kind, name, where);
    }
}
