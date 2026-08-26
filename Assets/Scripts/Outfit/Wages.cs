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
    /// Two rates, one figure: the books pay a WEEK at a time (payday is the seventh
    /// day - see OutfitDirector's day tick), while the street and the paper quote a
    /// DAY. Everything derives from the weekly envelope so the two can never drift.
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

        /// <summary>Weeks of wage a man who advertises wants in his hand before he
        /// starts - the signing money, and the classified column's whole barrier to
        /// entry. A month is what the period's own help-wanted columns asked.</summary>
        public const int WeeksDown = 4;

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

            if (member.Rank == Rank.Lieutenant)
                return LieutenantWage;

            var above = member.TotalHalfSteps() -
                        AttributeScale.Count * AttributeScale.MinHalfSteps;
            return HoodBase + HoodPerHalfStep * (above > 0 ? above : 0);
        }

        public static int WeeklyPayroll(Roster roster)
        {
            if (roster == null)
                return 0;

            var total = 0;
            for (var i = 0; i < roster.Members.Count; i++)
                total += WageFor(roster.Members[i]);
            return total;
        }

        /// <summary>
        /// A weekly envelope as the day rate the street quotes. Rounded UP, on purpose:
        /// a paper that advertised a man for a dollar less than he costs would be
        /// lying to the reader seven times a week.
        /// </summary>
        public static int PerDay(int weekly) =>
            (weekly + Campaign.DaysPerWeek - 1) / Campaign.DaysPerWeek;

        public static int DailyWageFor(Character member) => PerDay(WageFor(member));

        /// <summary>What the whole roster costs for one day - the figure a realtime
        /// campaign burns through while the week's payday is still coming.</summary>
        public static int DailyPayroll(Roster roster) => PerDay(WeeklyPayroll(roster));

        /// <summary>
        /// What a man advertising in the classified column asks a week: the
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

        /// <summary>The signing money for a weekly ask - <see cref="WeeksDown"/> weeks
        /// up front, out of the safe, before he has worked a day.</summary>
        public static int SigningFee(int weekly) => weekly * WeeksDown;
    }
}
