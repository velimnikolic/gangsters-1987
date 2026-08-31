using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>One man lost a step. The mirror of <see cref="Improvement"/>, written
    /// when the year takes something off him, never re-derived - the ledger and the
    /// newspaper print it the same way they print a rise.</summary>
    public readonly struct Decline
    {
        public readonly int CharacterId;
        public readonly string Name;
        public readonly CharacterAttribute Attribute;

        /// <summary>Half-steps AFTER the loss.</summary>
        public readonly int HalfSteps;

        /// <summary>What he turned on the birthday that took it.</summary>
        public readonly int Age;

        public Decline(int characterId, string name, CharacterAttribute attribute,
            int halfSteps, int age)
        {
            CharacterId = characterId;
            Name = name;
            Attribute = attribute;
            HalfSteps = halfSteps;
            Age = age;
        }
    }

    /// <summary>
    /// What the years take. Past his middle forties a man's hands, his eyes and his
    /// nerve go; what he knows about running other men does not. So Combat, Driving and
    /// Awareness fall away on his birthdays and Leadership, Organization, Street
    /// Authority and Connections hold - which is the whole reason an outfit has to
    /// promote its old fighters or carry them, and the reason it has to keep finding
    /// young ones.
    ///
    /// Every loss is PRINTED. Nothing about a man may change behind the player's back:
    /// he reads that his best gun is slowing down, and decides what to do about it.
    ///
    /// Pure and free of UnityEngine and of the Outfit layer both - the calendar is
    /// passed in, so the Personnel core stays a thing the headless suite can run.
    /// </summary>
    public static class Aging
    {
        /// <summary>The last year nothing is taken. From the birthday AFTER this one
        /// the field trades start going.</summary>
        public const int PeakAge = 45;

        /// <summary>The band a man's age is rolled in when he is dealt.</summary>
        public const int MinAge = 18;

        public const int MaxAge = 55;

        /// <summary>How many draws the age roll keeps the lowest of - two, so the
        /// street is full of young men and a man over forty is somebody who has already
        /// survived something.</summary>
        public const int AgeDraws = 2;

        /// <summary>The years between one loss and the next. Rolled per man: one ages
        /// gracefully and gives up a half-step every other year, the next gives one up
        /// every year.</summary>
        public const int MinWearYears = 1;

        public const int MaxWearYears = 2;

        /// <summary>What the years take.</summary>
        public static readonly CharacterAttribute[] FieldTrades =
        {
            CharacterAttribute.Combat,
            CharacterAttribute.Driving,
            CharacterAttribute.Awareness,
        };

        /// <summary>What they never touch. Named rather than derived as "the rest", so
        /// adding a skill to the enum cannot quietly enrol it in either list.</summary>
        public static readonly CharacterAttribute[] CommandTrades =
        {
            CharacterAttribute.Leadership,
            CharacterAttribute.Organization,
            CharacterAttribute.StreetAuthority,
            CharacterAttribute.Connections,
        };

        /// <summary>
        /// Deals a man his date of birth off his own stream - no draw taken from
        /// whatever sequence the caller is walking, the same discipline the hidden
        /// ceilings keep. The YEAR is stored rather than an age, so a long soak cannot
        /// drift it and a save cannot lose it.
        /// </summary>
        public static void RollBirth(Character man, int stream, int currentYear,
            int daysPerYear)
        {
            if (man == null)
                return;

            var rng = new System.Random(Potential.Mix(stream, 7_001));

            var age = MaxAge;
            for (var d = 0; d < AgeDraws; d++)
            {
                var draw = rng.Next(MinAge, MaxAge + 1);
                if (draw < age)
                    age = draw;
            }

            man.BirthYear = currentYear - age;
            man.BirthDayOfYear = daysPerYear > 0 ? rng.Next(daysPerYear) : 0;
        }

        /// <summary>How many years between one loss and the next, for this man.</summary>
        public static int WearYears(Character man)
        {
            if (man == null)
                return MaxWearYears;
            var rng = new System.Random(Potential.Mix(man.BirthYear * 397 + man.Id, 7_002));
            return rng.Next(MinWearYears, MaxWearYears + 1);
        }

        public static int AgeOn(Character man, int year) =>
            man == null || man.BirthYear <= 0 ? 0 : year - man.BirthYear;

        /// <summary>
        /// One day of the calendar. Only the men whose birthday it is are looked at,
        /// and only those past <see cref="PeakAge"/> lose anything - so on 363 days out
        /// of 364 this walks the roster and writes nothing.
        ///
        /// The dead and the deserted are off the books for everything. The jailed and
        /// the hospitalized are not: a year in a cell ages a man exactly as fast.
        ///
        /// The loss ignores a man's ceiling - what he could once have reached has no
        /// bearing on what the years take - but never takes him below one star, which
        /// is the floor the whole scale is built on.
        /// </summary>
        public static void Tick(Roster roster, int year, int dayOfYear, List<Decline> into)
        {
            if (roster == null)
                return;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Gone || member.BirthYear <= 0 ||
                    member.BirthDayOfYear != dayOfYear)
                    continue;

                var age = AgeOn(member, year);
                if (age <= PeakAge)
                    continue;

                var wear = WearYears(member);
                if (wear < MinWearYears)
                    wear = MinWearYears;
                if ((age - PeakAge) % wear != 0)
                    continue;

                for (var t = 0; t < FieldTrades.Length; t++)
                {
                    var trade = FieldTrades[t];
                    var at = member.GetHalfSteps(trade);
                    if (at <= AttributeScale.MinHalfSteps)
                        continue;

                    member.SetHalfSteps(trade, at - 1);
                    into?.Add(new Decline(member.Id, member.FullName, trade,
                        member.GetHalfSteps(trade), age));
                }
            }
        }
    }
}
