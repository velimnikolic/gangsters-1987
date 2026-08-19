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

        public static int WageFor(Character member)
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
    }
}
