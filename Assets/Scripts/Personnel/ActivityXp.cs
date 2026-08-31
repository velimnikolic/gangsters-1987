namespace LivingCity.Personnel
{
    /// <summary>
    /// What a man can be sent to do, in the terms the improvement system cares about.
    /// Deliberately NOT the order table's list: an order is a thing the player writes in
    /// the book, and several of them teach exactly the same lesson - a raid, a killing
    /// and an ambush are all attacking a rival - while two of the rows here (the wheel
    /// on a getaway, and holding a command) never appear in the book at all. The order
    /// layer maps its own types onto these; nothing maps the other way.
    /// </summary>
    public enum Activity
    {
        BlockPatrol,
        RacketCollection,
        Leaning,
        ShipmentTransport,
        Getaway,
        AttackOnARival,
        CommandingACrew,
        Negotiation,
        Scouting,
        BodyguardDuty,
        RunningABusiness,
        Recruiting,
    }

    /// <summary>How a job ended. A botch still teaches, less.</summary>
    public enum XpOutcome
    {
        Completed,

        /// <summary>It happened, but not as ordered - the deviation an undisciplined
        /// man produces, and the half-paid collection.</summary>
        Partial,

        Failed,
    }

    /// <summary>
    /// How dangerous the work is. An ordered scale rather than a description, because
    /// the balance law is an ordering: sorted by what it teaches must equal sorted by
    /// what it might cost, or grinding a man up becomes free.
    /// </summary>
    public enum Risk
    {
        None = 0,

        /// <summary>An incident, a seizure, a deal that goes badly - it costs the
        /// outfit, not the man.</summary>
        Slight = 1,

        /// <summary>Heat, witnesses, being seen where he should not have been.</summary>
        Real = 2,

        /// <summary>A wreck, a cell, or a grave.</summary>
        Grave = 3,
    }

    /// <summary>One row of the table: what the work teaches, what it pays, what it
    /// risks.</summary>
    public readonly struct ActivityRow
    {
        public readonly Activity Activity;

        /// <summary>Every skill the work trains. The points are banked against each of
        /// them, not divided between them - a night driving a shipment across town is a
        /// full night of driving AND a full night of watching the mirrors.</summary>
        public readonly CharacterAttribute[] Trains;

        /// <summary>What one completed piece of this work is worth.</summary>
        public readonly int BaseXp;

        public readonly Risk Risk;

        /// <summary>True for the one row a man earns without being exposed to anything:
        /// holding a command. Exempt from the danger ordering, because it is the
        /// exception the whole ordering exists to keep honest.</summary>
        public readonly bool Passive;

        public ActivityRow(Activity activity, CharacterAttribute[] trains, int baseXp,
            Risk risk, bool passive = false)
        {
            Activity = activity;
            Trains = trains;
            BaseXp = baseXp;
            Risk = risk;
            Passive = passive;
        }
    }

    /// <summary>
    /// The single source of truth for what work teaches. Every point of practice any
    /// system banks comes off this table - there is no second place a number lives, and
    /// no call site that decides for itself what a night's work was worth.
    ///
    /// Pure and free of UnityEngine, so the headless suite can run a scripted campaign
    /// and assert a man's whole sheet to the point.
    ///
    /// THE NUMBERS HERE ARE PLACEHOLDERS. They are three tiers apart in the right order
    /// and nothing more; the pace they produce - how many months of patrols make a
    /// watchful man - is XP-004's business, and it sets them from measured timelines
    /// against the growth curve. Change them there, in one place, and nowhere else.
    /// </summary>
    public static class ActivityXp
    {
        /// <summary>Dull work that carries no real danger.</summary>
        public const int LowXp = 3;

        /// <summary>Work that draws attention.</summary>
        public const int MidXp = 6;

        /// <summary>Work a man can fail to come home from.</summary>
        public const int HighXp = 12;

        /// <summary>Men per extra share of the command drip. Commanding thirty teaches
        /// more than commanding two, but nothing like fifteen times more - the drip is
        /// scaled by (1 + men / this), in integers.</summary>
        public const int CommandSpanDivisor = 25;

        /// <summary>
        /// The table. Read against the design spec's activity list; the two rows the
        /// spec does not name - bodyguard duty, and the getaway as its own work - come
        /// from RANK-003 and from the fact that this game has a street layer where a man
        /// drives away from things.
        /// </summary>
        public static readonly ActivityRow[] Rows =
        {
            new ActivityRow(Activity.BlockPatrol,
                new[] { CharacterAttribute.Awareness, CharacterAttribute.Streetwise,
                        CharacterAttribute.Organization },
                LowXp, Risk.None),

            new ActivityRow(Activity.ShipmentTransport,
                new[] { CharacterAttribute.Driving, CharacterAttribute.Awareness },
                LowXp, Risk.Slight),

            new ActivityRow(Activity.RacketCollection,
                new[] { CharacterAttribute.Intimidation, CharacterAttribute.Streetwise },
                LowXp, Risk.Slight),

            new ActivityRow(Activity.BodyguardDuty,
                new[] { CharacterAttribute.Awareness, CharacterAttribute.Combat },
                LowXp, Risk.Slight),

            // Not in the spec's nine. The order book has work the spec's list does not
            // describe - premises, licences, books, and going out to find a man - and
            // the rule is that missing work gets a ROW, never a number invented at the
            // call site.
            new ActivityRow(Activity.RunningABusiness,
                new[] { CharacterAttribute.Streetwise, CharacterAttribute.Organization,
                        CharacterAttribute.Awareness },
                LowXp, Risk.Slight),

            new ActivityRow(Activity.Recruiting,
                new[] { CharacterAttribute.Awareness, CharacterAttribute.Streetwise,
                        CharacterAttribute.StreetAuthority },
                LowXp, Risk.None),

            new ActivityRow(Activity.Negotiation,
                new[] { CharacterAttribute.Persuasion, CharacterAttribute.Connections },
                MidXp, Risk.Slight),

            new ActivityRow(Activity.Leaning,
                new[] { CharacterAttribute.Intimidation, CharacterAttribute.Combat },
                MidXp, Risk.Real),

            new ActivityRow(Activity.Scouting,
                new[] { CharacterAttribute.Awareness, CharacterAttribute.Stealth,
                        CharacterAttribute.Streetwise },
                MidXp, Risk.Real),

            new ActivityRow(Activity.Getaway,
                new[] { CharacterAttribute.Driving },
                HighXp, Risk.Grave),

            new ActivityRow(Activity.AttackOnARival,
                new[] { CharacterAttribute.Combat },
                HighXp, Risk.Grave),

            // The one row a man earns by being somewhere rather than doing something.
            new ActivityRow(Activity.CommandingACrew,
                new[] { CharacterAttribute.Leadership, CharacterAttribute.Organization,
                        CharacterAttribute.StreetAuthority },
                MidXp, Risk.None, passive: true),
        };

        public static ActivityRow RowOf(Activity activity)
        {
            for (var i = 0; i < Rows.Length; i++)
                if (Rows[i].Activity == activity)
                    return Rows[i];
            return Rows[0];
        }

        /// <summary>
        /// What one piece of this work is worth, by how it ended. A botch teaches a
        /// third of what a clean job does and a half-done one two thirds, rounded up,
        /// so failing at dangerous work still out-teaches succeeding at safe work -
        /// otherwise grinding by failing would be the cheapest training there is.
        /// </summary>
        public static int Points(Activity activity, XpOutcome outcome)
        {
            var baseXp = RowOf(activity).BaseXp;
            switch (outcome)
            {
                case XpOutcome.Completed:
                    return baseXp;
                case XpOutcome.Partial:
                    return (baseXp * 2 + 2) / 3;
                default:
                    return (baseXp + 2) / 3;
            }
        }

        /// <summary>
        /// Banks one piece of work with one man. The only door practice comes through -
        /// nothing else in the codebase may call <see cref="Character.AddPractice"/>
        /// with a number of its own.
        ///
        /// The dead and the deserted learn nothing; the jailed and the hospitalized are
        /// not sent anywhere to learn it either, so the caller decides who was there and
        /// this only guards the obvious.
        /// </summary>
        /// <returns>Points banked against each skill the work trains; 0 when nothing
        /// was banked.</returns>
        public static int Award(Character man, Activity activity, XpOutcome outcome)
        {
            if (man == null || man.Gone)
                return 0;

            var points = Points(activity, outcome);
            if (points <= 0)
                return 0;

            var row = RowOf(activity);
            for (var i = 0; i < row.Trains.Length; i++)
                man.AddPractice(row.Trains[i], points);
            return points;
        }

        /// <summary>
        /// A day of holding a command. Scaled by how many men answer to him - thirty is
        /// a heavier job than two - but sub-linearly, so a big crew is not a shortcut
        /// to a five-star lieutenant.
        /// </summary>
        public static int AwardCommand(Character man, int menCommanded)
        {
            if (man == null || man.Gone || menCommanded <= 0)
                return 0;

            var points = Points(Activity.CommandingACrew, XpOutcome.Completed) *
                         (1 + menCommanded / CommandSpanDivisor);
            var row = RowOf(Activity.CommandingACrew);
            for (var i = 0; i < row.Trains.Length; i++)
                man.AddPractice(row.Trains[i], points);
            return points;
        }
    }
}
