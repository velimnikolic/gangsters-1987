using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The payroll table. Pure and derived-at-read on purpose: the Finances page is
    /// specified to compute wages from the ACTUAL roster at render, so hiring or losing
    /// a man changes the number the same frame - a stored payroll figure is exactly the
    /// stale-display bug the spec bans.
    ///
    /// ONE TABLE (WAGE-001). <see cref="HouseRate"/> is the whole of it, and both
    /// <see cref="WageFor"/> and <see cref="WorthOf"/> read it: what the house pays a
    /// man it raised IS what a man of his rank, his trades and his tenure is worth, so
    /// nobody on the house scale can ever be underpaid. A pay gap exists only where a
    /// BARGAIN sits below the rate - a man hired out of the newspaper whose stars have
    /// outgrown the price he printed, and nothing else. Before this the two methods
    /// were two formulas that disagreed by construction, which declared every
    /// lieutenant in the game underpaid from his first morning.
    ///
    /// The shape of the table is the game's core money pressure. It reads RANK first
    /// and TRADE second:
    ///
    ///   * a hood is paid for what he is BEST at - his three best trades, not his
    ///     eleven numbers. A corner boy is a corner boy however many things he is
    ///     mediocre at, and the old sum-of-eleven paid him like a made man for it;
    ///   * a lieutenant is paid on the four trades a crew is actually run with, off a
    ///     base that is higher than the hood ceiling, so he out-earns every one of his
    ///     men BY CONSTRUCTION and never by a per-crew rule;
    ///   * both of them draw a small capped premium for time on the books
    ///     (<see cref="TenureBonus"/>, WAGE-004) - a man who has stood on the corner
    ///     for a year is not the man who signed this morning.
    ///
    /// The dead come off the books while the jailed and the hospitalized stay on them -
    /// you pay the men inside, because an outfit that doesn't is an outfit that gets
    /// informed on.
    ///
    /// ONE rate, and it is a DAY's. The campaign runs in real time and the books close
    /// every midnight, so there is no envelope to wait for and no weekly figure for a
    /// daily one to be derived from and drift against. Every wage on this table, on the
    /// roster's WAGE column and in the classified column is what a man draws for one
    /// day on the books.
    ///
    /// Every figure here is documented in Docs/economy-prices.md §1, which is the
    /// authority; a constant changed here without changing the doc is a constant
    /// nobody can defend later.
    /// </summary>
    public static class Wages
    {
        /// <summary>A hood's floor - the corner-boy rate, at one star in everything.
        /// The 1987 anchor is a full-time street soldier a little over a documented
        /// gang enforcer's ~$1,000 a month.</summary>
        public const int HoodBase = 40;

        /// <summary>Per half-step of his THREE BEST trades above an all-one-star man
        /// (6 half-steps across three): talent is payroll, but only the talent he is
        /// actually used for. Range 40-136 a day.</summary>
        public const int HoodPerHalfStep = 4;

        /// <summary>How many of a hood's trades he is paid for. Three, because a man
        /// is hired for what he does and no crew ever asks him for the other eight.</summary>
        public const int HoodTradesPaid = 3;

        /// <summary>A lieutenant's floor - above the hood ceiling (136) so a promotion
        /// is always a rise and a demotion always a cut. The 1987 anchor is a
        /// documented local gang leader at $4,000-11,000 a month, or $133-367 a
        /// day.</summary>
        public const int LieutenantBase = 150;

        /// <summary>Per half-step of the four trades a crew is run with, above an
        /// all-one-star head (8 half-steps across four). Range 150-342 a day.</summary>
        public const int LieutenantPerHalfStep = 6;

        /// <summary>The four trades a lieutenant is actually paid for: who will follow
        /// him, what he notices, whether the gear reaches his men, and what the street
        /// concedes him on sight. His gun hand is not on the list - he is not the one
        /// holding it any more.</summary>
        static readonly CharacterAttribute[] LieutenantTrades =
        {
            CharacterAttribute.Leadership,
            CharacterAttribute.Awareness,
            CharacterAttribute.Organization,
            CharacterAttribute.StreetAuthority,
        };

        /// <summary>A retained professional's flat fee. NOTE: no code path assigns
        /// either specialty today - the accountant and the lawyer come through their
        /// own doors, which are not built - so these two numbers are the doc's price
        /// waiting for a door and are deliberately outside the rank scale.</summary>
        public const int AccountantWage = 250;

        /// <summary>See <see cref="AccountantWage"/>: no door assigns it yet.</summary>
        public const int LawyerWage = 400;

        // -------------------------------------------------------------- WAGE-004

        /// <summary>What a month on the books adds to a man's envelope, hood and
        /// lieutenant alike. Small on purpose: it separates two identical men by their
        /// service without ever competing with rank or trade.</summary>
        public const int TenurePerMonth = 2;

        /// <summary>Days to a month, for the tenure premium. The campaign's own
        /// calendar year is longer than twelve of these; a "month" here is a round
        /// thirty days of service and nothing to do with the almanac's dates.</summary>
        public const int TenureDays = 30;

        /// <summary>The ceiling on service pay - about ten months, and then a man is
        /// as long-serving as the books will pay him for. Uncapped it would eventually
        /// out-earn the whole trade scale and make a mediocre veteran the most
        /// expensive man in the outfit.</summary>
        public const int TenureCap = 20;

        // -------------------------------------------------------------- WAGE-002

        /// <summary>What a man who advertises charges over the house rate, percent: a
        /// quarter, for walking in ready-made instead of being built. The house rate
        /// is what the outfit pays a man it raised itself; this is the premium on a
        /// stranger who arrives already able to run a crew.</summary>
        public const int AskPremiumPercent = 125;

        /// <summary>Days of wage a man who advertises wants in his hand before he
        /// starts - the signing money, and the classified column's whole barrier to
        /// entry. A fortnight: at the WAGE-001 rates a month down was nearly $10,000,
        /// two fifths of the opening safe, and an outfit that signed one man out of the
        /// paper could not reach its first collection.</summary>
        public const int DaysDown = 14;

        // -------------------------------------------------------------- WAGE-003

        /// <summary>Nights running with an empty envelope before a hood stops turning
        /// up at all (<see cref="CharacterStatus.Deserted"/>).</summary>
        public const int DesertAfterUnpaidNights = 3;

        /// <summary>And before a lieutenant starts listening to somebody else. Longer
        /// than a hood's patience because he has a crew and a branch to lose, and
        /// because what he does about it is worse: he takes them with him.</summary>
        public const int DefectAfterUnpaidNights = 5;

        /// <summary>What a night with nothing in the envelope costs the outfit in his
        /// loyalty. Landed the night it happens rather than folded into the seven-day
        /// drift: an unpaid man is an EVENT, not a modifier.</summary>
        public const int UnpaidLoyaltyHit = 3;

        // ---------------------------------------------------------- the one table

        /// <summary>
        /// THE house rate: what a man of this rank, these trades and this much service
        /// costs the outfit for one day. The single source both <see cref="WageFor"/>
        /// and <see cref="WorthOf"/> read, so the two can never disagree again.
        /// </summary>
        /// <param name="day">The campaign day, for the service premium. 0 - the
        /// default everywhere a caller has no calendar - reads the rate with no tenure
        /// on it, which is what a man on his first day draws anyway.</param>
        public static int HouseRate(Character man, int day = 0) =>
            man == null ? 0 : HouseRateAs(man, man.Rank, day);

        /// <summary>The house rate for a man at a rank he is not necessarily standing
        /// at yet. The classified column needs it: an ad is priced as the lieutenant
        /// the man advertises as, and the price has to be readable before the
        /// promotion that makes him one.</summary>
        public static int HouseRateAs(Character man, Rank rank, int day = 0)
        {
            if (man == null || man.Gone)
                return 0;

            switch (man.Specialty)
            {
                case Specialty.Accountant:
                    return AccountantWage;
                case Specialty.Lawyer:
                    return LawyerWage;
            }

            // The player character owns the payroll; he does not draw an envelope from it.
            if (rank == Rank.Boss)
                return 0;

            var rate = rank == Rank.Lieutenant
                ? LieutenantBase + LieutenantPerHalfStep * HeadHalfStepsAbove(man)
                : HoodBase + HoodPerHalfStep * TopTradeHalfStepsAbove(man);

            return rate + TenureBonus(man, day);
        }

        /// <summary>His three best trades, in half-steps above an all-one-star man.
        /// Never negative: the scale's own floor is one star everywhere.</summary>
        static int TopTradeHalfStepsAbove(Character man)
        {
            // Three passes over eleven numbers rather than a sort: no allocation, and
            // the wage table is read on every repaint of every roster row.
            var first = 0;
            var second = 0;
            var third = 0;
            for (var a = 0; a < AttributeScale.Count; a++)
            {
                var value = man.GetHalfSteps((CharacterAttribute)a);
                if (value > first)
                {
                    third = second;
                    second = first;
                    first = value;
                }
                else if (value > second)
                {
                    third = second;
                    second = value;
                }
                else if (value > third)
                {
                    third = value;
                }
            }

            var above = first + second + third -
                        HoodTradesPaid * AttributeScale.MinHalfSteps;
            return above > 0 ? above : 0;
        }

        /// <summary>The four command trades, in half-steps above an all-one-star
        /// head.</summary>
        static int HeadHalfStepsAbove(Character man)
        {
            var total = 0;
            for (var i = 0; i < LieutenantTrades.Length; i++)
                total += man.GetHalfSteps(LieutenantTrades[i]);

            var above = total - LieutenantTrades.Length * AttributeScale.MinHalfSteps;
            return above > 0 ? above : 0;
        }

        /// <summary>
        /// WAGE-004. What time on the books is worth a day, capped. Read off the day
        /// his file says he joined (<see cref="Career.JoinedDay"/>) - nothing new is
        /// stored on the man, and a man dealt with no joining day counts from day one,
        /// which is what the founding fixture is.
        /// </summary>
        public static int TenureBonus(Character man, int day)
        {
            if (man == null || day <= 0)
                return 0;

            var joined = Career.JoinedDay(man);
            if (joined <= 0)
                joined = 1;

            var months = (day - joined) / TenureDays;
            if (months <= 0)
                return 0;

            var bonus = TenurePerMonth * months;
            return bonus > TenureCap ? TenureCap : bonus;
        }

        /// <summary>Whole months he has been on the books - what the dossier prints
        /// beside the premium.</summary>
        public static int MonthsOnTheBooks(Character man, int day)
        {
            if (man == null || day <= 0)
                return 0;
            var joined = Career.JoinedDay(man);
            if (joined <= 0)
                joined = 1;
            var months = (day - joined) / TenureDays;
            return months > 0 ? months : 0;
        }

        // ------------------------------------------------------------ what he draws

        public static int WageFor(Character member, int day = 0)
        {
            if (member == null || member.Gone)
                return 0;

            // A man sent out of the city draws nothing while he is away (GAN-222,
            // FLEE-006). Deliberately not the same as jailed or hurt: those two DO draw
            // their day, because the outfit looks after its own - and a man the player
            // chose to put on a bus to get him off a wanted sheet is a cost avoided, not
            // a man being looked after.
            if (member.OutOfTown)
                return 0;

            // A man hired out of the paper is paid what he advertised. The house scale
            // is what the outfit pays the men it raised itself; the ad was a bargain
            // struck once, and it follows him until his rank changes or he is granted
            // the rate (WAGE-002). A bargain draws no service premium either - his ask
            // was struck for the man he was.
            if (member.WageAsked > 0)
                return member.WageAsked;

            return HouseRate(member, day);
        }

        /// <summary>What the whole roster draws for one day - the outfit's burn, and
        /// the figure the blotter's runway divides the safe by. Reads the roster's own
        /// campaign day, so the service premium is on it without any caller having to
        /// remember the calendar.</summary>
        public static int DailyPayroll(Roster roster)
        {
            if (roster == null)
                return 0;

            var total = 0;
            for (var i = 0; i < roster.Members.Count; i++)
                total += WageFor(roster.Members[i], roster.Day);
            return total;
        }

        /// <summary>How many men on the books went home with an empty envelope and
        /// have not been paid since (WAGE-003). Derived off the men, like every other
        /// figure here - the rail and the balance sheet both print it, and neither
        /// stores it.</summary>
        public static int UnpaidCount(Roster roster)
        {
            if (roster == null)
                return 0;

            var count = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (!man.Gone && man.UnpaidSince > 0)
                    count++;
            }
            return count;
        }

        /// <summary>What those men are owed for one day.</summary>
        public static int UnpaidWages(Roster roster)
        {
            if (roster == null)
                return 0;

            var total = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (!man.Gone && man.UnpaidSince > 0)
                    total += WageFor(man, roster.Day);
            }
            return total;
        }

        /// <summary>What one man draws for one day. The table IS the day rate, so this
        /// is <see cref="WageFor"/> - kept as a name callers can read a unit off.</summary>
        public static int DailyWageFor(Character member, int day = 0) =>
            WageFor(member, day);

        /// <summary>
        /// What a man of his rank, his trades and his service is WORTH a day - the
        /// house rate, and the SAME arithmetic that pays him (WAGE-001). A man on the
        /// house scale therefore reads 0 short of it, always; a gap can only open under
        /// a bargain that has fallen behind the man who struck it.
        /// </summary>
        public static int WorthOf(Character member, int day = 0) =>
            HouseRate(member, day);

        /// <summary>What he is short of the rate, a day; 0 when he is paid it or
        /// better. The one figure the greed ladder is climbed by.</summary>
        public static int PayGap(Character member, int day = 0)
        {
            var gap = WorthOf(member, day) - WageFor(member, day);
            return gap > 0 ? gap : 0;
        }

        /// <summary>
        /// What a man advertising in the classified column asks a day: the house rate
        /// for the lieutenant he advertises as, plus one market premium
        /// (<see cref="AskPremiumPercent"/>). ONE price list - the ad can no longer
        /// quote a figure the wage table would not charge, which is what made a
        /// demoted paper lieutenant a $350 hood for the rest of his life.
        /// </summary>
        public static int AskFor(Character man, int day = 0)
        {
            if (man == null)
                return 0;

            // Priced as the lieutenant he advertises as, whatever rank he is standing
            // at while the ad is being set: PersonnelDirector brings him on as a hood
            // for exactly as long as it takes to promote him, and re-stamps the ask
            // after that promotion returns.
            return HouseRateAs(man, Rank.Lieutenant, day) * AskPremiumPercent / 100;
        }

        /// <summary>The signing money for a daily ask - <see cref="DaysDown"/> days up
        /// front, out of the safe, before he has worked one of them.</summary>
        public static int SigningFee(int daily) => daily * DaysDown;
    }
}
