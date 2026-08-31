using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The payroll table. Pure and derived-at-read on purpose: the Finances page is
    /// specified to compute wages from the ACTUAL roster at render, so hiring or losing
    /// a man changes the number the same frame - a stored payroll figure is exactly the
    /// stale-display bug the spec bans.
    ///
    /// The shape of the table is the game's core money pressure: every hood costs more
    /// the better he is, a lieutenant costs a flat premium, and the dead come off the
    /// books while the jailed and the hospitalized stay on them - you pay the men
    /// inside, because an outfit that doesn't is an outfit that gets informed on.
    ///
    /// ONE rate, and it is a DAY's. The campaign runs in real time and the books close
    /// every midnight, so there is no envelope to wait for and no weekly figure for a
    /// daily one to be derived from and drift against. Every wage on this table, on the
    /// roster's WAGE column and in the classified column is what a man draws for one
    /// day on the books.
    /// </summary>
    public static class Wages
    {
        /// <summary>A hood's floor - the corner-boy rate.</summary>
        public const int HoodBase = 60;

        /// <summary>Per half-step above an all-twos man (22 total): talent is payroll.</summary>
        public const int HoodPerHalfStep = 5;

        public const int LieutenantWage = 200;
        public const int AccountantWage = 250;
        public const int LawyerWage = 400;

        /// <summary>Days of wage a man who advertises wants in his hand before he
        /// starts - the signing money, and the classified column's whole barrier to
        /// entry. A month is what the period's own help-wanted columns asked, and a
        /// month is twenty-eight days of it.</summary>
        public const int DaysDown = 28;

        public static int WageFor(Character member)
        {
            if (member == null || member.Gone)
                return 0;

            // A man hired out of the paper is paid what he advertised. The scale below
            // is what the outfit pays the men it raised itself; the ad was a bargain
            // struck once, and it follows him for as long as he is on the books.
            if (member.WageAsked > 0)
                return member.WageAsked;

            switch (member.Specialty)
            {
                case Specialty.Accountant:
                    return AccountantWage;
                case Specialty.Lawyer:
                    return LawyerWage;
            }

            // The player character owns the payroll; he does not draw an envelope from it.
            if (member.Rank == Rank.Boss)
                return 0;

            if (member.Rank == Rank.Lieutenant)
                return LieutenantWage;

            var above = member.TotalHalfSteps() -
                        AttributeScale.Count * AttributeScale.MinHalfSteps;
            return HoodBase + HoodPerHalfStep * (above > 0 ? above : 0);
        }

        /// <summary>What the whole roster draws for one day - the outfit's burn, and
        /// the figure the blotter's runway divides the safe by.</summary>
        public static int DailyPayroll(Roster roster)
        {
            if (roster == null)
                return 0;

            var total = 0;
            for (var i = 0; i < roster.Members.Count; i++)
                total += WageFor(roster.Members[i]);
            return total;
        }

        /// <summary>What one man draws for one day. The table IS the day rate, so this
        /// is <see cref="WageFor"/> - kept as a name callers can read a unit off.</summary>
        public static int DailyWageFor(Character member) => WageFor(member);

        /// <summary>
        /// What a man of his rank and his stats is WORTH a day, as against what the
        /// outfit happens to be paying him. Not the same number, and the gap between
        /// them is the whole of PSY-003: a lieutenant draws a flat premium however good
        /// he gets, and a man hired out of the paper draws the bargain he struck years
        /// ago. Both of them can work out what they are worth.
        /// </summary>
        public static int WorthOf(Character member)
        {
            if (member == null || member.Gone)
                return 0;

            switch (member.Specialty)
            {
                case Specialty.Accountant:
                    return AccountantWage;
                case Specialty.Lawyer:
                    return LawyerWage;
            }

            // The player character owns the payroll; there is nothing for him to be
            // short of.
            if (member.Rank == Rank.Boss)
                return 0;

            var above = member.TotalHalfSteps() -
                        AttributeScale.Count * AttributeScale.MinHalfSteps;
            if (above < 0)
                above = 0;

            return member.Rank == Rank.Lieutenant
                ? LieutenantWage + HoodPerHalfStep * above
                : HoodBase + HoodPerHalfStep * above;
        }

        /// <summary>What he is short of the rate, a day; 0 when he is paid it or
        /// better. The one figure the greed ladder is climbed by.</summary>
        public static int PayGap(Character member)
        {
            var gap = WorthOf(member) - WageFor(member);
            return gap > 0 ? gap : 0;
        }

        /// <summary>
        /// What a man advertising in the classified column asks a day: the
        /// lieutenancy's premium PLUS the same talent scale a hood is paid on. The
        /// house rate for a lieutenant is flat because the house raised him; a man who
        /// walks in off an advertisement prices his own eleven stats, which is what
        /// makes one ad in the column worth more than another.
        /// </summary>
        public static int AskFor(Character man)
        {
            if (man == null)
                return 0;

            var above = man.TotalHalfSteps() -
                        AttributeScale.Count * AttributeScale.MinHalfSteps;
            return LieutenantWage + HoodPerHalfStep * (above > 0 ? above : 0);
        }

        /// <summary>The signing money for a daily ask - <see cref="DaysDown"/> days up
        /// front, out of the safe, before he has worked one of them.</summary>
        public static int SigningFee(int daily) => daily * DaysDown;
    }
}
